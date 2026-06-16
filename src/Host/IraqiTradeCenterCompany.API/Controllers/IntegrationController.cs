using IraqiTradeCenterCompany.API.Auth.Permissions;
using IraqiTradeCenterCompany.API.Integration;
using IraqiTradeCenterCompany.API.Licensing;
using IraqiTradeCenterCompany.Modules.Store.Application.Features.ReceiveIncomingOrder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace IraqiTradeCenterCompany.API.Controllers;

[ApiController]
[Route("api/integration")]
public class IntegrationController : BaseApiController
{
    private readonly IConfiguration _cfg;
    private readonly ILicenseService _license;
    private readonly ParentIntegrationOptions _parentOpts;

    public IntegrationController(IConfiguration cfg, ILicenseService license, IOptions<ParentIntegrationOptions> parentOpts)
    {
        _cfg = cfg;
        _license = license;
        _parentOpts = parentOpts.Value;
    }

    /// <summary>حالة التكامل — للمدير داخل لوحة الشركة.</summary>
    [Authorize]
    [RequirePermission(PermissionRegistry.System.CompanySettings.Read)]
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var licCfg = await _license.GetConfigAsync(ct);
        var licStatus = await _license.GetStatusAsync(ct);
        var parentDb = await TryParentSubscriberAsync(licCfg.CompanyKey, ct);

        var apiPublic = (_cfg["CompanyInstance:PublicSiteUrl"]
                         ?? _cfg["App:CompanyApiUrl"]
                         ?? "https://api_iraqitradecenter_company.gcc.iq").TrimEnd('/');

        return Ok(new
        {
            success = true,
            data = new
            {
                parentApiUrl = _parentOpts.ApiBaseUrl,
                parentSiteUrl = _parentOpts.SiteUrl,
                companyKey = licCfg.CompanyKey,
                licenseActive = licStatus.IsActive,
                licenseEndDate = licStatus.EndDateUtc,
                parentSubscriber = parentDb,
                integrationHeader = _parentOpts.IntegrationHeaderName ?? "X-ITC-Integration-Key",
                webhookIncomingOrders = $"{apiPublic}/api/integration/incoming-orders",
                authKeyHint = MaskKey(licCfg.AuthKey),
                features = new
                {
                    incomingOrders = true,
                    ssoLogin = false,
                    licensePushFromParent = false,
                }
            }
        });
    }

    /// <summary>استقبال طلبية من النظام الرئيسي (تاجر / منصة أم).</summary>
    [AllowAnonymous]
    [IntegrationAuth]
    [HttpPost("incoming-orders")]
    public async Task<IActionResult> ReceiveIncomingOrder([FromBody] ReceiveIncomingOrderBody body, CancellationToken ct)
    {
        var cmd = new ReceiveIncomingOrderCommand(
            body.PlatformOrderId,
            body.PlatformOrderNumber,
            body.PlatformTraderId,
            body.TotalAmount,
            body.Items.Select(i => new ReceiveIncomingOrderItemDto(
                i.ItemId, i.ItemName, i.UnitId, i.Quantity, i.UnitPrice)).ToList(),
            body.Customer is null ? null : new ReceiveIncomingOrderCustomerDto(
                body.Customer.PlatformUserId,
                body.Customer.Code,
                body.Customer.BusinessName,
                body.Customer.OwnerName,
                body.Customer.Phone,
                body.Customer.Email));

        var result = await Mediator.Send(cmd, ct);
        if (!result.IsSuccess)
            return BadRequest(new { success = false, errors = new[] { result.Error ?? "فشل استلام الطلبية" } });

        return Ok(new { success = true, data = result.Value });
    }

    private async Task<object?> TryParentSubscriberAsync(string companyKey, CancellationToken ct)
    {
        var parentCs = _cfg.GetConnectionString("ParentConnection");
        if (string.IsNullOrWhiteSpace(parentCs)) return null;

        try
        {
            await using var cn = new SqlConnection(parentCs);
            await cn.OpenAsync(ct);
            await using var cmd = cn.CreateCommand();
            cmd.CommandText = """
                SELECT TOP 1 DatabaseName, AuthKey, Active,
                       CONVERT(varchar(10), StartDate, 23) AS StartDate,
                       CONVERT(varchar(10), EndDate, 23) AS EndDate
                FROM dbo.T_Subscribers
                WHERE DatabaseName = @db OR AuthKey = @ak
                """;
            var dbName = _cfg["License:DatabaseName"] ?? _cfg.GetSection("CompanyInstance")["Database"];
            cmd.Parameters.AddWithValue("@db", (object?)dbName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ak", companyKey);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return new { found = false };
            return new
            {
                found = true,
                databaseName = r.GetString(0),
                active = r.GetBoolean(2),
                startDate = r.IsDBNull(3) ? null : r.GetString(3),
                endDate = r.IsDBNull(4) ? null : r.GetString(4),
            };
        }
        catch
        {
            return new { found = false, error = "تعذّر الاتصال بقاعدة النظام الرئيسي" };
        }
    }

    public sealed class ReceiveIncomingOrderBody
    {
        public Guid PlatformOrderId { get; set; }
        public string PlatformOrderNumber { get; set; } = "";
        public Guid PlatformTraderId { get; set; }
        public decimal TotalAmount { get; set; }
        public List<ReceiveIncomingOrderItemBody> Items { get; set; } = new();
        public ReceiveIncomingOrderCustomerBody? Customer { get; set; }
    }

    public sealed class ReceiveIncomingOrderItemBody
    {
        public int ItemId { get; set; }
        public string ItemName { get; set; } = "";
        public int UnitId { get; set; }
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public sealed class ReceiveIncomingOrderCustomerBody
    {
        public Guid PlatformUserId { get; set; }
        public string Code { get; set; } = "";
        public string BusinessName { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public string Phone { get; set; } = "";
        public string? Email { get; set; }
    }

    private static string MaskKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "—";
        if (key.Length <= 8) return "****";
        return $"{key[..4]}…{key[^4..]}";
    }
}
