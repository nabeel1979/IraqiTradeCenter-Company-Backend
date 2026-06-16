using IraqiTradeCenterCompany.API.Auth.Auditing;
using IraqiTradeCenterCompany.API.Auth.Permissions;
using IraqiTradeCenterCompany.API.Controllers;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IraqiTradeCenterCompany.API.Settings;

[Route("api/settings/email")]
public class EmailSettingsController : BaseApiController
{
    private readonly IEmailSettingsService _service;
    private readonly IEmailSmtpSender _sender;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _perms;
    private readonly IAuditLogger _audit;

    public EmailSettingsController(
        IEmailSettingsService service,
        IEmailSmtpSender sender,
        ICurrentUserService currentUser,
        IPermissionService perms,
        IAuditLogger audit)
    {
        _service = service;
        _sender = sender;
        _currentUser = currentUser;
        _perms = perms;
        _audit = audit;
    }

    public class EmailSettingsDto
    {
        public bool IsEnabled { get; set; }
        public string Provider { get; set; } = "Zoho";
        public string SmtpHost { get; set; } = "smtp.zoho.com";
        public int SmtpPort { get; set; } = 587;
        public string SecurityMode { get; set; } = "StartTls";
        public string? Username { get; set; }
        public string? AppPasswordMasked { get; set; }
        public bool AppPasswordSet { get; set; }
        public string? FromEmail { get; set; }
        public string? FromDisplayName { get; set; }
        public string? ReplyToEmail { get; set; }
        public string? SignatureHtml { get; set; }
        public string? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class UpdateEmailSettingsRequest
    {
        public bool? IsEnabled { get; set; }
        public string? Provider { get; set; }
        public string? SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public string? SecurityMode { get; set; }
        public string? Username { get; set; }
        /// <summary>فارغ = الإبقاء على القديم.</summary>
        public string? AppPassword { get; set; }
        public string? FromEmail { get; set; }
        public string? FromDisplayName { get; set; }
        public string? ReplyToEmail { get; set; }
        public string? SignatureHtml { get; set; }
    }

    public class TestEmailRequest
    {
        public string? ToEmail { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!await CanReadAsync(ct)) return Forbid();
        var row = await _service.GetAsync(ct);
        return Ok(new { success = true, data = ToDto(row) });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateEmailSettingsRequest req, CancellationToken ct)
    {
        if (!await CanWriteAsync(ct)) return Forbid();

        var saved = await _service.UpdateAsync(row =>
        {
            if (req.IsEnabled.HasValue) row.IsEnabled = req.IsEnabled.Value;
            if (!string.IsNullOrWhiteSpace(req.Provider))
                row.Provider = req.Provider.Trim() is "Custom" ? "Custom" : "Zoho";
            if (!string.IsNullOrWhiteSpace(req.SmtpHost)) row.SmtpHost = req.SmtpHost.Trim();
            if (req.SmtpPort is > 0 and <= 65535) row.SmtpPort = req.SmtpPort.Value;
            if (!string.IsNullOrWhiteSpace(req.SecurityMode))
            {
                var sm = req.SecurityMode.Trim();
                row.SecurityMode = sm.Equals("Ssl", StringComparison.OrdinalIgnoreCase) ? "Ssl" : "StartTls";
            }
            if (req.Username != null) row.Username = NullIfEmpty(req.Username);
            if (!string.IsNullOrWhiteSpace(req.AppPassword)) row.AppPassword = req.AppPassword.Trim();
            if (req.FromEmail != null) row.FromEmail = NullIfEmpty(req.FromEmail);
            if (req.FromDisplayName != null) row.FromDisplayName = NullIfEmpty(req.FromDisplayName);
            if (req.ReplyToEmail != null) row.ReplyToEmail = NullIfEmpty(req.ReplyToEmail);
            if (req.SignatureHtml != null) row.SignatureHtml = string.IsNullOrWhiteSpace(req.SignatureHtml) ? null : req.SignatureHtml;
        }, _currentUser.FullName ?? _currentUser.UserId?.ToString(), ct);

        _service.Invalidate();

        await _audit.LogAsync(
            entityType: "EmailSettings",
            entityId: "1",
            action: AuditActions.Update,
            summary: "تحديث إعدادات البريد الإلكتروني (Zoho)",
            details: new { saved.IsEnabled, saved.Provider, host = saved.SmtpHost, port = saved.SmtpPort },
            ct: ct);

        return Ok(new { success = true, data = ToDto(saved) });
    }

    /// <summary>اختبار SMTP: إرسال رسالة تجريبية (افتراضياً إلى المرسل نفسه).</summary>
    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] TestEmailRequest? req, CancellationToken ct)
    {
        if (!await CanReadAsync(ct)) return Forbid();
        var row = await _service.GetAsync(ct);
        var to = NullIfEmpty(req?.ToEmail) ?? row.FromEmail?.Trim() ?? row.Username?.Trim();
        if (string.IsNullOrWhiteSpace(to))
            return Ok(new { success = false, message = "حدّد بريد المستلم للاختبار أو عيّن From/Username." });

        var sig = string.IsNullOrWhiteSpace(row.SignatureHtml) ? "" : $"<hr/>{row.SignatureHtml}";
        var html = $"""
            <p>هذه رسالة اختبار من <strong>مركز التجارة العراقي</strong>.</p>
            <p>إذا وصلتك، فإعدادات Zoho Mail صحيحة وجاهزة لإرسال الردود الإلكترونية.</p>
            <p style="color:#666;font-size:12px;">{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>
            {sig}
            """;

        var (ok, message, detail) = await _sender.SendAsync(row, to!, "اختبار البريد — Iraqi Trade Center", html, ct: ct);
        return Ok(new { success = ok, message, detail, toEmail = to });
    }

    private async Task<bool> CanReadAsync(CancellationToken ct)
    {
        if (_currentUser.IsSuperAdmin) return true;
        var uid = _currentUser.UserId;
        if (uid is null) return false;
        return await _perms.HasPermissionAsync(uid.Value, PermissionRegistry.System.CompanySettings.Read, ct)
            || await _perms.HasPermissionAsync(uid.Value, PermissionRegistry.System.CompanySettings.Update, ct);
    }

    private async Task<bool> CanWriteAsync(CancellationToken ct)
    {
        if (_currentUser.IsSuperAdmin) return true;
        var uid = _currentUser.UserId;
        if (uid is null) return false;
        return await _perms.HasPermissionAsync(uid.Value, PermissionRegistry.System.CompanySettings.Update, ct);
    }

    private static EmailSettingsDto ToDto(EmailSettings row) => new()
    {
        IsEnabled = row.IsEnabled,
        Provider = row.Provider,
        SmtpHost = row.SmtpHost,
        SmtpPort = row.SmtpPort,
        SecurityMode = row.SecurityMode,
        Username = row.Username,
        AppPasswordMasked = Mask(row.AppPassword),
        AppPasswordSet = !string.IsNullOrWhiteSpace(row.AppPassword),
        FromEmail = row.FromEmail,
        FromDisplayName = row.FromDisplayName,
        ReplyToEmail = row.ReplyToEmail,
        SignatureHtml = row.SignatureHtml,
        UpdatedAtUtc = row.UpdatedAtUtc?.ToString("o"),
        UpdatedBy = row.UpdatedBy,
    };

    private static string? Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return null;
        if (secret.Length <= 4) return new string('*', secret.Length);
        return new string('*', Math.Min(secret.Length - 4, 12)) + secret[^4..];
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
