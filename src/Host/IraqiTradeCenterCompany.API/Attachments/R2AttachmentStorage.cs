using System;
using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using IraqiTradeCenterCompany.API.Settings;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.Extensions.Logging;

namespace IraqiTradeCenterCompany.API.Attachments;

/// <summary>
/// مخزن مرفقات يكتب إلى Cloudflare R2 عبر بروتوكول S3-متوافق. الإعدادات تأتي
/// ديناميكياً من <see cref="IAttachmentSettingsService"/> فلا يحتاج المستخدم
/// تعديل <c>appsettings.json</c>: يكفي حفظ مفاتيح R2 من صفحة الإعدادات.
///
/// <para>
/// الـ <c>ServiceURL</c> = <c>https://{AccountId}.r2.cloudflarestorage.com</c>
/// مع <c>ForcePathStyle=true</c> و توقيع <c>SignatureVersion="4"</c>.
/// نُغلِق الـ <see cref="AmazonS3Client"/> بعد كل عملية بدلاً من تخزينه؛ تكلفة
/// إنشائه ضئيلة وهذا يضمن أن أي تعديل فوري على الإعدادات يأخذ مفعوله.
/// </para>
/// </summary>
public class R2AttachmentStorage : IAttachmentStorage
{
    private readonly IAttachmentSettingsService _settings;
    private readonly ILogger<R2AttachmentStorage> _log;

    public R2AttachmentStorage(
        IAttachmentSettingsService settings,
        ILogger<R2AttachmentStorage> log)
    {
        _settings = settings;
        _log = log;
    }

    public string ProviderName => "R2";

    private Task<(IAmazonS3 client, string bucket)> CreateClientAsync(CancellationToken ct)
        => CreateClientAsync(forDiagnostic: false, ct);

    /// <summary>
    /// إنشاء عميل S3-متوافق لـ Cloudflare R2.
    /// عند <paramref name="forDiagnostic"/>=true: نُلغي إعادة المحاولة ونُقصّر
    /// timeouts إلى ~25 ثانية لكي يفشل اختبار الاتصال بسرعة بدلاً من ~90+ ث
    /// (3 محاولات × backoff). للاستخدام الإنتاجي العادي نَترك القيم الافتراضية
    /// المتسامحة الموصى بها من AWS SDK.
    /// </summary>
    private async Task<(IAmazonS3 client, string bucket)> CreateClientAsync(bool forDiagnostic, CancellationToken ct)
    {
        var row = await _settings.GetAsync(ct);
        if (string.IsNullOrWhiteSpace(row.R2AccountId)
            || string.IsNullOrWhiteSpace(row.R2AccessKeyId)
            || string.IsNullOrWhiteSpace(row.R2SecretAccessKey)
            || string.IsNullOrWhiteSpace(row.R2Bucket))
        {
            throw new InvalidOperationException("R2 settings are incomplete. Configure AccountId, AccessKey, Secret, and Bucket from the Settings page.");
        }

        var serviceUrl = R2EndpointHelper.BuildServiceUrl(row.R2AccountId!, row.R2Jurisdiction);
        var creds = new BasicAWSCredentials(row.R2AccessKeyId!.Trim(), row.R2SecretAccessKey!.Trim());
        var cfg = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            // R2 يتطلب AuthenticationRegion = "auto" لتجنّب أخطاء توقيع المنطقة.
            AuthenticationRegion = "auto",
            SignatureVersion = "4",
            UseHttp = false,
            // ‎على Windows Server حيث TLS 1.3 يُفعّل post-quantum في Schannel،
            // ‎نُجبر TLS 1.2 و HTTP/1.1 لضمان handshake مع Cloudflare R2.
            HttpClientFactory = forDiagnostic
                ? TlsCompatibleHttpClientFactory.Diagnostic
                : TlsCompatibleHttpClientFactory.Instance,
            // ‎الإنتاج: 3 محاولات افتراضية. الاختبار: 0 لكي يُرجع الخطأ فوراً.
            MaxErrorRetry = forDiagnostic ? 0 : 3,
            // ‎مهلة إجمالية على مستوى الطلب الواحد (يكمّل HttpClient.Timeout).
            Timeout = forDiagnostic ? TimeSpan.FromSeconds(25) : TimeSpan.FromMinutes(5),
        };
        var client = new AmazonS3Client(creds, cfg);
        return (client, row.R2Bucket!.Trim());
    }

    /// <summary>
    /// نسخة "تشخيصية" من <see cref="CreateClientAsync(CancellationToken)"/>:
    /// زمن قصير + بدون retries، تستعملها <c>AttachmentSettingsController.TestR2Connection</c>.
    /// </summary>
    internal Task<(IAmazonS3 client, string bucket)> CreateDiagnosticClientAsync(CancellationToken ct)
        => CreateClientAsync(forDiagnostic: true, ct);

    /// <summary>
    /// مصنع <see cref="HttpClient"/> مخصص للـ AWS SDK، مبني على
    /// <see cref="SocketsHttpHandler"/> المضمَّن في .NET 8 (لا dependency خارجي).
    ///
    /// <para><b>التوافق مع Windows Server:</b> فرض <c>TLS 1.2</c> فقط على
    /// مستوى <see cref="System.Net.Security.SslClientAuthenticationOptions.EnabledSslProtocols"/>
    /// يتفادى بشكل قاطع post-quantum hybrid key exchange (Kyber/ML-KEM)
    /// الذي يُفعّله Schannel افتراضياً مع TLS 1.3 على Windows Server 2022/2025،
    /// والذي يكسر handshake مع <c>*.r2.cloudflarestorage.com</c>. حصر البروتوكول
    /// على 1.2 يضمن مصافحة موثوقة على كل أجيال Windows Server دون أي مكتبة إضافية.</para>
    ///
    /// <para><b>HTTP/1.1 صريح:</b> Cloudflare R2 يدعم HTTP/2 لكن AWS SDK لا يربح
    /// شيئاً من multiplexing لأن كل طلب مستقل. نُجبر 1.1 لتفادي مشاكل ALPN
    /// المتقطّعة على Schannel.</para>
    ///
    /// <para><b>AutoDecompression off:</b> AWS SDK يفكّ ضغط الرد بنفسه؛ تركه
    /// للـ handler يُسبّب double-decompression أو فشل توقيع.</para>
    /// </summary>
    private sealed class TlsCompatibleHttpClientFactory : Amazon.Runtime.HttpClientFactory
    {
        /// <summary>للاستخدام الإنتاجي: timeouts متسامحة (5 دقائق).</summary>
        public static readonly TlsCompatibleHttpClientFactory Instance = new(diagnostic: false);
        /// <summary>للاختبار اليدوي: timeouts قصيرة (~25 ث) لإرجاع الخطأ بسرعة.</summary>
        public static readonly TlsCompatibleHttpClientFactory Diagnostic = new(diagnostic: true);

        private readonly bool _diagnostic;
        private TlsCompatibleHttpClientFactory(bool diagnostic) { _diagnostic = diagnostic; }

        public override HttpClient CreateHttpClient(IClientConfig clientConfig)
        {
            var connectTimeout = _diagnostic ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(30);
            var totalTimeout = _diagnostic ? TimeSpan.FromSeconds(25) : TimeSpan.FromMinutes(5);
            var targetHost = ResolveTargetHost(clientConfig.ServiceURL);

            var handler = new SocketsHttpHandler
            {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    // ‎مفتاح التوافق مع Windows Server: TLS 1.2 فقط ⇒ بلا
                    // ‎post-quantum و بلا تعقيدات Schannel TLS 1.3.
                    EnabledSslProtocols = SslProtocols.Tls12,
                    // ‎Cloudflare R2 يتطلّب SNI صريحاً على endpoint الحساب.
                    TargetHost = targetHost,
                },
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                ConnectTimeout = connectTimeout,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                MaxConnectionsPerServer = 8,
            };
            var http = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = totalTimeout,
                DefaultRequestVersion = System.Net.HttpVersion.Version11,
                DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower,
            };
            return http;
        }

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => true;

        private static string? ResolveTargetHost(string? serviceUrl)
        {
            if (string.IsNullOrWhiteSpace(serviceUrl)) return null;
            return Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri) ? uri.Host : null;
        }
    }

    /// <summary>
    /// نسخة "تشخيصية" من <see cref="UploadWithKeyAsync"/>: تستعمل عميلاً بـ
    /// timeouts قصيرة و 0 retries كي يُرجع الخطأ بسرعة لـ endpoint الاختبار.
    /// </summary>
    public async Task UploadWithKeyDiagnosticAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateDiagnosticClientAsync(ct);
        try
        {
            var put = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = content,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                AutoCloseStream = false,
                AutoResetStreamPosition = false,
                DisablePayloadSigning = true,
            };
            await client.PutObjectAsync(put, ct);
        }
        finally { client.Dispose(); }
    }

    /// <summary>نسخة تشخيصية من <see cref="OpenReadAsync"/> بنفس فلسفة الـ timeouts القصيرة.</summary>
    public async Task<Stream> OpenReadDiagnosticAsync(string storageKey, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateDiagnosticClientAsync(ct);
        try
        {
            var resp = await client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucket,
                Key = storageKey,
            }, ct);
            var ms = new MemoryStream();
            await resp.ResponseStream.CopyToAsync(ms, ct);
            ms.Position = 0;
            return ms;
        }
        finally { client.Dispose(); }
    }

    /// <summary>نسخة تشخيصية من <see cref="DeleteAsync"/> بنفس فلسفة الـ timeouts القصيرة.</summary>
    public async Task DeleteDiagnosticAsync(string storageKey, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateDiagnosticClientAsync(ct);
        try
        {
            await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucket,
                Key = storageKey,
            }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        finally { client.Dispose(); }
    }

public record R2ObjectEntry(string Key, long SizeBytes, DateTime LastModifiedUtc);

    public async Task<IReadOnlyList<R2ObjectEntry>> ListObjectsWithPrefixAsync(string prefix, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateClientAsync(ct);
        try
        {
            var results = new List<R2ObjectEntry>();
            string? token = null;
            do
            {
                var req = new ListObjectsV2Request
                {
                    BucketName = bucket,
                    Prefix = prefix,
                    ContinuationToken = token,
                };
                var resp = await client.ListObjectsV2Async(req, ct);
                foreach (var o in resp.S3Objects)
                {
                    if (string.IsNullOrEmpty(o.Key) || o.Key.EndsWith('/')) continue;
                    results.Add(new R2ObjectEntry(
                        o.Key,
                        o.Size,
                        o.LastModified));
                }
                token = resp.IsTruncated ? resp.NextContinuationToken : null;
            } while (!string.IsNullOrEmpty(token));

            return results;
        }
        finally
        {
            client.Dispose();
        }
    }

    public async Task<string> SaveAsync(string logicalFolder, string suggestedFileName, Stream content, string? contentType, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateClientAsync(ct);
        try
        {
            var safeFolder = SanitizeFolder(logicalFolder);
            var safeName = SanitizeFileName(suggestedFileName);
            var unique = $"{Guid.NewGuid():N}_{safeName}";
            var key = string.IsNullOrEmpty(safeFolder) ? unique : $"{safeFolder}/{unique}";

            var put = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = content,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                AutoCloseStream = false,
                AutoResetStreamPosition = false,
                DisablePayloadSigning = true, // R2 يفضّل عدم توقيع الـ payload لأن streaming-signed-payload غير مدعوم بالكامل
            };
            await client.PutObjectAsync(put, ct);
            return key;
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// رفع كائن إلى R2 باستخدام مفتاح موجود مسبقاً (بدون إعادة توليد). تُستخدم
    /// من سيرفس المزامنة عند نقل ملف من القرص المحلي إلى R2 — يجب أن يحتفظ
    /// الـ key بالشكل ذاته كي يبقى الـ <c>StorageKey</c> ثابتاً ما بين القراءة
    /// المحلية والقراءة من R2 لاحقاً.
    /// </summary>
    public async Task UploadWithKeyAsync(string key, Stream content, string? contentType, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateClientAsync(ct);
        try
        {
            var put = new PutObjectRequest
            {
                BucketName = bucket,
                Key = key,
                InputStream = content,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
                AutoCloseStream = false,
                AutoResetStreamPosition = false,
                DisablePayloadSigning = true,
            };
            await client.PutObjectAsync(put, ct);
        }
        finally
        {
            client.Dispose();
        }
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateClientAsync(ct);
        try
        {
            var resp = await client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucket,
                Key = storageKey,
            }, ct);
            // ‎نُحمّل المحتوى لذاكرة وسيطة ثم نُغلق العميل والاستجابة، حتى لا نُخاطر
            // ‎بقطع الاتصال أثناء الـ streaming للعميل النهائي. حدّ المرفقات المضبوط
            // ‎(25MB افتراضياً) يجعل هذا آمناً للذاكرة.
            var ms = new MemoryStream();
            await resp.ResponseStream.CopyToAsync(ms, ct);
            ms.Position = 0;
            return ms;
        }
        finally
        {
            client.Dispose();
        }
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var (client, bucket) = await CreateClientAsync(ct);
        try
        {
            await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucket,
                Key = storageKey,
            }, ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // ‎غير موجود ⇒ تسامحاً نَعتبرها "محذوفة" (idempotent).
            _log.LogInformation("R2 delete: object not found (treated as deleted) {Key}", storageKey);
        }
        finally
        {
            client.Dispose();
        }
    }

    private static string SanitizeFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return string.Empty;
        var clean = new string(folder.Select(c =>
            c == '/' || c == '_' || c == '-' || char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return clean.Replace("..", "_").Trim('/');
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var arr = (name ?? "file").Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(arr).Trim();
        if (string.IsNullOrWhiteSpace(s)) s = "file";
        if (s.Length > 200) s = s[..200];
        return s;
    }
}
