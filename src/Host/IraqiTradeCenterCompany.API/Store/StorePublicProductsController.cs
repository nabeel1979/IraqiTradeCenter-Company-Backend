using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace IraqiTradeCenterCompany.API.Store;

/// <summary>
/// نقاط عامة (بدون مصادقة) تخدم منتجات وصور المتجر <b>مباشرة من باك إند الشركة</b>،
/// دون المرور بالباك إند الأم. يحلّ التينانت حسب كود الشركة عبر سجل المشتركين في
/// قاعدة بيانات الأم (<c>IraqiTradeCenter.dbo.T_Subscribers</c>) ثم يستعلم من قاعدة
/// بيانات الشركة المعنية على نفس الخادم.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/store/products")]
public class StorePublicProductsController : ControllerBase
{
    private const string MasterDatabase = "IraqiTradeCenter";

    private readonly IConfiguration _config;
    private readonly ILogger<StorePublicProductsController> _log;

    public StorePublicProductsController(IConfiguration config, ILogger<StorePublicProductsController> log)
    {
        _config = config;
        _log = log;
    }

    private sealed class R2Creds
    {
        public string Provider = "Local";
        public string? LocalRoot;
        public string? AccountId, AccessKey, Secret, Bucket;
        public string Jurisdiction = "default";
        public bool R2Complete => !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(AccessKey)
            && !string.IsNullOrWhiteSpace(Secret) && !string.IsNullOrWhiteSpace(Bucket);
    }

    private async Task<R2Creds?> ReadStorageSettingsAsync(string databaseName, CancellationToken ct)
    {
        var cs = new SqlConnectionStringBuilder(MasterConnectionString()) { InitialCatalog = databaseName }.ConnectionString;
        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT TOP 1 Provider, LocalRootPath, R2AccountId, R2AccessKeyId,
                            R2SecretAccessKey, R2Bucket, R2Jurisdiction
                            FROM auth.AttachmentStorageSettings ORDER BY Id";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new R2Creds
        {
            Provider     = r.IsDBNull(0) ? "Local" : r.GetString(0),
            LocalRoot    = r.IsDBNull(1) ? null : r.GetString(1),
            AccountId    = r.IsDBNull(2) ? null : r.GetString(2),
            AccessKey    = r.IsDBNull(3) ? null : r.GetString(3),
            Secret       = r.IsDBNull(4) ? null : r.GetString(4),
            Bucket       = r.IsDBNull(5) ? null : r.GetString(5),
            Jurisdiction = r.IsDBNull(6) ? "default" : r.GetString(6),
        };
    }

    // GET /api/store/products?search=&company=&pageNumber=1&pageSize=16
    [HttpGet]
    public async Task<IActionResult> ListProducts(
        [FromQuery] string? search,
        [FromQuery] string? company,
        [FromQuery] string? companyCode,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 16,
        CancellationToken ct = default)
    {
        var filterCode = string.IsNullOrWhiteSpace(company) ? companyCode : company;
        pageSize = Math.Clamp(pageSize, 1, 50);
        pageNumber = Math.Max(1, pageNumber);

        var companies = await GetActiveCompaniesAsync(filterCode, ct);
        var allProducts = new List<StorePublicProductDto>();

        foreach (var c in companies)
        {
            try { allProducts.AddRange(await GetCompanyProductsAsync(c, search, ct)); }
            catch { /* skip company if DB unreachable */ }
        }

        var total = allProducts.Count;
        var items = allProducts
            .OrderBy(p => p.CompanyName).ThenBy(p => p.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new { items, totalCount = total, pageNumber, pageSize });
    }

    // GET /api/store/products/{companyCode}/{id}
    [HttpGet("{companyCode}/{id:int}")]
    public async Task<IActionResult> GetProduct(string companyCode, int id, CancellationToken ct)
    {
        var companies = await GetActiveCompaniesAsync(companyCode, ct);
        var company = companies.FirstOrDefault();
        if (company is null) return NotFound(new { message = "الشركة غير موجودة" });

        var products = await GetCompanyProductsAsync(company, null, ct, id);
        var product = products.FirstOrDefault();
        if (product is null) return NotFound(new { message = "المنتج غير موجود" });

        return Ok(product);
    }

    // GET /api/store/products/{companyCode}/{id}/image
    [HttpGet("{companyCode}/{id:int}/image")]
    public async Task<IActionResult> GetProductImage(string companyCode, int id, CancellationToken ct)
    {
        var companies = await GetActiveCompaniesAsync(companyCode, ct);
        var company = companies.FirstOrDefault();
        if (company is null) return NotFound();

        var builder = new SqlConnectionStringBuilder(MasterConnectionString()) { InitialCatalog = company.DatabaseName };

        string? storageKey;
        await using (var cn = new SqlConnection(builder.ConnectionString))
        {
            await cn.OpenAsync(ct);
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT MainImageStorageKey FROM inv.Items WHERE Id = @Id AND IsDeleted = 0";
            cmd.Parameters.AddWithValue("@Id", id);
            storageKey = (await cmd.ExecuteScalarAsync(ct)) as string;
        }
        if (string.IsNullOrWhiteSpace(storageKey)) return NotFound();

        var contentType = GuessContentType(storageKey);
        Response.Headers["Cache-Control"] = "public, max-age=86400";

        // إعدادات التخزين: نقرأ من قاعدة الشركة ومن قاعدة الأم (الباك إند المشترك
        // يرفع الكائنات بإعدادات الأم)، ونجرّب أي مفاتيح R2 مكتملة منهما.
        var companySettings = await ReadStorageSettingsAsync(company.DatabaseName, ct);
        var masterSettings  = await ReadStorageSettingsAsync(MasterDatabase, ct);

        var isR2 = string.Equals(companySettings?.Provider, "R2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(masterSettings?.Provider, "R2", StringComparison.OrdinalIgnoreCase);

        if (isR2)
        {
            // نُجرّب مفاتيح الشركة أولاً ثم الأم (الكائن قد يكون في أيٍّ منهما).
            foreach (var s in new[] { companySettings, masterSettings })
            {
                if (s is null || !s.R2Complete) continue;
                try
                {
                    var bytes = await FetchFromR2Async(s.AccountId!, s.AccessKey!, s.Secret!, s.Bucket!, s.Jurisdiction, storageKey!, ct);
                    return File(bytes, contentType);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "R2 fetch failed for {Company} key {Key} bucket {Bucket}", company.CompanyCode, storageKey, s.Bucket);
                }
            }
            return NotFound();
        }

        // Local disk
        var localRoot = companySettings?.LocalRoot ?? masterSettings?.LocalRoot;
        var root = string.IsNullOrWhiteSpace(localRoot) ? "C:/ITC-Uploads/attachments" : localRoot!;
        var rel = storageKey!.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootFull = Path.GetFullPath(root);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return NotFound();
        if (!System.IO.File.Exists(full)) return NotFound();

        var localBytes = await System.IO.File.ReadAllBytesAsync(full, ct);
        return File(localBytes, contentType);
    }

    /// <summary>
    /// يجلب كائناً من Cloudflare R2 (S3-متوافق) ببيانات اعتماد الشركة. يفرض TLS 1.2
    /// و HTTP/1.1 لضمان المصافحة على Windows Server (يتفادى post-quantum في Schannel).
    /// </summary>
    private static async Task<byte[]> FetchFromR2Async(
        string accountId, string accessKey, string secret, string bucket,
        string? jurisdiction, string key, CancellationToken ct)
    {
        var j = (jurisdiction ?? "default").Trim().ToLowerInvariant();
        var host = (j is "eu" or "eu-jurisdiction" or "fedramp")
            ? $"{accountId.Trim()}.eu.r2.cloudflarestorage.com"
            : $"{accountId.Trim()}.r2.cloudflarestorage.com";
        var serviceUrl = $"https://{host}";

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12,
                TargetHost = host,
            },
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        using var http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(2),
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };

        var cfg = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "auto",
            SignatureVersion = "4",
            UseHttp = false,
            HttpClientFactory = new SimpleHttpClientFactory(http),
            MaxErrorRetry = 2,
            Timeout = TimeSpan.FromMinutes(2),
        };
        using var client = new AmazonS3Client(new BasicAWSCredentials(accessKey.Trim(), secret.Trim()), cfg);
        var b = bucket.Trim();

        try
        {
            return await GetObjectBytesAsync(client, b, key, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // المفتاح المخزّن قد لا يطابق المفتاح الفعلي حرفياً (مسافات/أقواس/ترميز).
            // نسرد الكائنات تحت نفس البادئة ونطابق بالـ GUID أو اسم الملف.
            var resolved = await ResolveKeyByListingAsync(client, b, key, ct);
            if (resolved is null) throw;
            return await GetObjectBytesAsync(client, b, resolved, ct);
        }
    }

    private static async Task<byte[]> GetObjectBytesAsync(AmazonS3Client client, string bucket, string key, CancellationToken ct)
    {
        using var resp = await client.GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, ct);
        using var ms = new MemoryStream();
        await resp.ResponseStream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>يطابق المفتاح الحقيقي بسرد البادئة <c>items/{id}/</c> ومطابقة GUID/اسم الملف.</summary>
    private static async Task<string?> ResolveKeyByListingAsync(AmazonS3Client client, string bucket, string key, CancellationToken ct)
    {
        var slash = key.LastIndexOf('/');
        var prefix = slash >= 0 ? key[..(slash + 1)] : "";
        var fileName = slash >= 0 ? key[(slash + 1)..] : key;
        var guid = fileName.Split('_', 2)[0]; // الجزء قبل أول '_' عادةً GUID

        var list = await client.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = bucket, Prefix = prefix, MaxKeys = 100 }, ct);
        var keys = list.S3Objects?.Select(o => o.Key).ToList() ?? new List<string>();
        if (keys.Count == 0) return null;

        // 1) مطابقة بالـ GUID  2) مطابقة بنهاية اسم الملف  3) إن وُجد كائن واحد فقط نستخدمه
        return keys.FirstOrDefault(k => !string.IsNullOrEmpty(guid) && k.Contains(guid, StringComparison.OrdinalIgnoreCase))
            ?? keys.FirstOrDefault(k => k.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
            ?? (keys.Count == 1 ? keys[0] : null);
    }

    private sealed class SimpleHttpClientFactory : Amazon.Runtime.HttpClientFactory
    {
        private readonly HttpClient _http;
        public SimpleHttpClientFactory(HttpClient http) { _http = http; }
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => _http;
        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }

    private string MasterConnectionString()
    {
        var cs = _config.GetConnectionString("DefaultConnection")!;
        return new SqlConnectionStringBuilder(cs) { InitialCatalog = MasterDatabase }.ConnectionString;
    }

    private async Task<List<CompanyInfo>> GetActiveCompaniesAsync(string? filterCode, CancellationToken ct)
    {
        await using var cn = new SqlConnection(MasterConnectionString());
        await cn.OpenAsync(ct);

        var whereCode = string.IsNullOrWhiteSpace(filterCode) ? "" : "AND CompanyCode = @Code";
        var sql = $"""
            SELECT CompanyCode, Dscrp, DatabaseName
            FROM dbo.T_Subscribers
            WHERE Active = 1 AND DbProvisioned = 1 AND CompanyCode IS NOT NULL
                  {whereCode}
            """;

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        if (!string.IsNullOrWhiteSpace(filterCode))
            cmd.Parameters.AddWithValue("@Code", filterCode.ToUpperInvariant());

        var list = new List<CompanyInfo>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new CompanyInfo(r.GetString(0), r.IsDBNull(1) ? r.GetString(0) : r.GetString(1), r.GetString(2)));

        return list;
    }

    private async Task<List<StorePublicProductDto>> GetCompanyProductsAsync(
        CompanyInfo company, string? search, CancellationToken ct, int? specificId = null)
    {
        var builder = new SqlConnectionStringBuilder(MasterConnectionString()) { InitialCatalog = company.DatabaseName };

        await using var cn = new SqlConnection(builder.ConnectionString);
        await cn.OpenAsync(ct);

        var searchFilter = string.IsNullOrWhiteSpace(search) ? "" : "AND (i.NameAr LIKE '%' + @Search + '%' OR i.Code LIKE '%' + @Search + '%')";
        var idFilter = specificId.HasValue ? "AND i.Id = @Id" : "";

        var sql = $"""
            SELECT
                i.Id, i.Code, i.NameAr, i.NameEn, i.Description,
                i.BaseSalesPrice, i.StockBaseQuantity,
                i.BaseUnitId, ub.NameAr, ub.NameEn,
                c.NameAr AS CategoryName,
                i.MainImageStorageKey,
                i.MediumUnitId, i.MediumUnitFactor, i.MediumSalesPrice, um.NameAr, um.NameEn,
                i.LargeUnitId, i.LargeUnitFactor, i.LargeSalesPrice, ul.NameAr, ul.NameEn
            FROM inv.Items i
            LEFT JOIN inv.UnitsOfMeasure ub ON ub.Id = i.BaseUnitId
            LEFT JOIN inv.UnitsOfMeasure um ON um.Id = i.MediumUnitId
            LEFT JOIN inv.UnitsOfMeasure ul ON ul.Id = i.LargeUnitId
            LEFT JOIN inv.ItemCategories c ON c.Id = i.CategoryId
            WHERE i.IsDeleted = 0
              AND i.IsActive = 1
              AND i.ShowInStore = 1
              AND i.IsAvailableForSale = 1
              AND i.StockBaseQuantity > 0
              {searchFilter}
              {idFilter}
            """;

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        if (!string.IsNullOrWhiteSpace(search)) cmd.Parameters.AddWithValue("@Search", search);
        if (specificId.HasValue) cmd.Parameters.AddWithValue("@Id", specificId.Value);

        var products = new List<StorePublicProductDto>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var id = r.GetInt32(0);
            var hasImage = !r.IsDBNull(11) && !string.IsNullOrWhiteSpace(r.GetString(11));
            var baseSalesPrice = r.GetDecimal(5);
            var baseUnitId = r.GetInt32(7);
            var baseUnitName = r.IsDBNull(8) ? "وحدة" : r.GetString(8);
            var baseUnitNameEn = r.IsDBNull(9) ? null : r.GetString(9);

            // قائمة الوحدات القابلة للاختيار: الأساسية دائماً، ثم المتوسطة/الكبيرة إن وُجدت.
            var units = new List<StoreProductUnitDto>
            {
                new() { UnitId = baseUnitId, Name = baseUnitName, NameEn = baseUnitNameEn, Price = baseSalesPrice, FactorToBase = 1m },
            };

            if (!r.IsDBNull(12)) // medium unit
            {
                var medFactor = r.IsDBNull(13) ? 1m : r.GetDecimal(13);
                var medPrice = r.IsDBNull(14) ? baseSalesPrice * medFactor : r.GetDecimal(14);
                units.Add(new StoreProductUnitDto
                {
                    UnitId = r.GetInt32(12),
                    Name = r.IsDBNull(15) ? "وحدة" : r.GetString(15),
                    NameEn = r.IsDBNull(16) ? null : r.GetString(16),
                    Price = medPrice,
                    FactorToBase = medFactor,
                });

                if (!r.IsDBNull(17)) // large unit (factor relative to medium)
                {
                    var lrgFactor = (r.IsDBNull(18) ? 1m : r.GetDecimal(18)) * medFactor;
                    var lrgPrice = r.IsDBNull(19) ? baseSalesPrice * lrgFactor : r.GetDecimal(19);
                    units.Add(new StoreProductUnitDto
                    {
                        UnitId = r.GetInt32(17),
                        Name = r.IsDBNull(20) ? "وحدة" : r.GetString(20),
                        NameEn = r.IsDBNull(21) ? null : r.GetString(21),
                        Price = lrgPrice,
                        FactorToBase = lrgFactor,
                    });
                }
            }

            products.Add(new StorePublicProductDto
            {
                Id = id,
                Code = r.GetString(1),
                Name = r.GetString(2),
                NameEn = r.IsDBNull(3) ? null : r.GetString(3),
                Description = r.IsDBNull(4) ? null : r.GetString(4),
                SellingPrice = baseSalesPrice,
                CurrentStock = r.GetDecimal(6),
                UnitOfMeasureId = baseUnitId,
                UnitOfMeasureName = baseUnitName,
                UnitOfMeasureNameEn = baseUnitNameEn,
                CategoryName = r.IsDBNull(10) ? null : r.GetString(10),
                ImageUrl = hasImage ? $"/capi/store/products/{company.CompanyCode}/{id}/image" : null,
                CompanyCode = company.CompanyCode,
                CompanyName = company.Name,
                ShowInStore = true,
                Units = units,
            });
        }

        return products;
    }

    private static string GuessContentType(string key)
    {
        var ext = Path.GetExtension(key).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
    }

    private record CompanyInfo(string CompanyCode, string Name, string DatabaseName);
}

public class StorePublicProductDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? NameEn { get; set; }
    public string? Description { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal CurrentStock { get; set; }
    public int UnitOfMeasureId { get; set; }
    public string UnitOfMeasureName { get; set; } = default!;
    public string? UnitOfMeasureNameEn { get; set; }
    public string? CategoryName { get; set; }
    public string? ImageUrl { get; set; }
    public string CompanyCode { get; set; } = default!;
    public string CompanyName { get; set; } = default!;
    public bool ShowInStore { get; set; }
    public List<StoreProductUnitDto> Units { get; set; } = new();
}

public class StoreProductUnitDto
{
    public int UnitId { get; set; }
    public string Name { get; set; } = default!;
    public string? NameEn { get; set; }
    public decimal Price { get; set; }
    /// <summary>كم وحدة أساسية تعادل وحدة واحدة من هذه الوحدة (لحساب المخزون المتاح).</summary>
    public decimal FactorToBase { get; set; }
}
