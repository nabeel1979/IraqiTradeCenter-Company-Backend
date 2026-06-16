using IraqiTradeCenterCompany.API.Auth.Permissions;
using IraqiTradeCenterCompany.API.ContactRegistry;
using IraqiTradeCenterCompany.API.Settings;
using IraqiTradeCenterCompany.SharedKernel.Contacts;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.API.Auth;

public interface IForgotPasswordService
{
    Task<(bool Success, string[] Errors)> SendTemporaryPasswordAsync(string username, CancellationToken ct = default);
}

public class ForgotPasswordService : IForgotPasswordService
{
    private readonly AuthDbContext _db;
    private readonly IEmailSettingsService _emailSettings;
    private readonly IEmailSmtpSender _smtp;
    private readonly IPermissionService _permissions;
    private readonly IResetCredentialLinkService _credentialLinks;

    public ForgotPasswordService(
        AuthDbContext db,
        IEmailSettingsService emailSettings,
        IEmailSmtpSender smtp,
        IPermissionService permissions,
        IResetCredentialLinkService credentialLinks)
    {
        _db = db;
        _emailSettings = emailSettings;
        _smtp = smtp;
        _permissions = permissions;
        _credentialLinks = credentialLinks;
    }

    public async Task<(bool Success, string[] Errors)> SendTemporaryPasswordAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return (false, new[] { "اسم المستخدم مطلوب" });

        var user = await FindActiveUserAsync(username.Trim(), ct);
        if (user is null)
            return (false, new[] { "اسم المستخدم غير موجود أو الحساب غير فعّال" });

        var emailRow = await _db.ContactPoints.AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.OwnerType == ContactOwnerTypes.User
                && c.OwnerId == user.Id.ToString()
                && c.Kind == ContactKinds.Email, ct);

        if (emailRow is null || string.IsNullOrWhiteSpace(emailRow.DisplayValue))
            return (false, new[] { "لا يوجد بريد إلكتروني مسجّل لهذا الحساب — تواصل مع مدير المنصة لإضافة بريدك" });

        var settings = await _emailSettings.GetAsync(ct);
        if (!settings.IsEnabled)
            return (false, new[] { "خدمة البريد غير مفعّلة في النظام — تواصل مع مدير المنصة" });

        var temporaryPassword = PasswordHelper.Generate();
        user.PasswordHash = PasswordHelper.Hash(temporaryPassword);
        user.MustChangePassword = true;
        await _db.SaveChangesAsync(ct);
        _permissions.InvalidateUser(user.Id);

        var loginName = user.Phone;
        var links = _credentialLinks.Create(loginName, temporaryPassword);
        var viewUrl = links.ViewUrl;

        var subject = "مركز التجارة العراقي — كلمة مرور مؤقتة";
        var html = BuildEmailHtml(user.FullName, loginName, temporaryPassword, viewUrl);
        var plain = BuildPlainText(user.FullName, loginName, temporaryPassword, viewUrl);

        var (sent, message, detail) = await _smtp.SendAsync(
            settings, emailRow.DisplayValue.Trim(), subject, html, plain, ct);

        if (!sent)
        {
            var err = string.IsNullOrWhiteSpace(detail) ? message : $"{message}: {detail}";
            return (false, new[] { err });
        }

        return (true, Array.Empty<string>());
    }

    private async Task<CompanyUser?> FindActiveUserAsync(string identifier, CancellationToken ct)
    {
        if (identifier.Contains('@', StringComparison.Ordinal))
        {
            var norm = ContactNormalizer.Normalize(ContactKinds.Email, identifier, out _, out _);
            if (norm is null) return null;

            var ownerId = await _db.ContactPoints.AsNoTracking()
                .Where(c => c.Kind == ContactKinds.Email
                            && c.NormalizedValue == norm
                            && c.OwnerType == ContactOwnerTypes.User)
                .Select(c => c.OwnerId)
                .FirstOrDefaultAsync(ct);

            if (ownerId is null || !Guid.TryParse(ownerId, out var uid)) return null;
            return await _db.Users.FirstOrDefaultAsync(u => u.Id == uid && u.IsActive, ct);
        }

        var lower = identifier.ToLowerInvariant();
        return await _db.Users.FirstOrDefaultAsync(u =>
            u.IsActive
            && (u.Phone.ToLower() == lower || u.FullName.ToLower() == lower), ct);
    }

    private static string BuildPlainText(string fullName, string loginName, string temporaryPassword, string viewUrl)
    {
        return $"""
            مركز التجارة العراقي — كلمة مرور مؤقتة

            مرحباً {fullName}،

            اسم المستخدم: {loginName}
            كلمة المرور المؤقتة: {temporaryPassword}

            للنسخ بضغطة واحدة افتح الرابط (صالح 30 دقيقة):
            {viewUrl}

            عند تسجيل الدخول سيُطلب تعيين كلمة مرور جديدة فوراً.
            """;
    }

    private static string BuildEmailHtml(string fullName, string loginName, string temporaryPassword, string viewUrl)
    {
        var safeName = System.Net.WebUtility.HtmlEncode(fullName);
        var safeLogin = System.Net.WebUtility.HtmlEncode(loginName);
        var safePwd = System.Net.WebUtility.HtmlEncode(temporaryPassword);
        var safeView = System.Net.WebUtility.HtmlEncode(viewUrl);
        var copyUserUrl = System.Net.WebUtility.HtmlEncode($"{viewUrl}?copy=username");
        var copyPwdUrl = System.Net.WebUtility.HtmlEncode($"{viewUrl}?copy=password");

        return $"""
            <!DOCTYPE html>
            <html dir="rtl" lang="ar">
            <head><meta charset="utf-8"/></head>
            <body style="font-family:'Segoe UI',Tahoma,Arial,sans-serif;background:#f0f0f0;padding:24px;margin:0;">
              <div style="max-width:480px;margin:0 auto;background:#fff;border-radius:8px;padding:28px 32px;border:1px solid #e5e5e5;">
                <h2 style="color:#c45c00;margin:0 0 12px;font-size:18px;font-weight:600;">مركز التجارة العراقي</h2>
                <p style="margin:0 0 8px;font-size:14px;color:#333;">مرحباً <strong>{safeName}</strong>،</p>
                <p style="margin:0 0 16px;font-size:13px;color:#555;line-height:1.5;">تم إنشاء كلمة مرور مؤقتة. عند تسجيل الدخول سيُطلب منك تعيين كلمة مرور جديدة فوراً.</p>

                <div style="background:#fdf8f3;border:1px solid #e8e0d4;border-radius:8px;padding:18px 20px;margin:0 0 20px;">
                  <p style="margin:0 0 14px;text-align:center;font-size:13px;font-weight:600;color:#333;">بيانات الدخول الجديدة</p>
                  {EmailCredentialRow("اسم المستخدم", safeLogin, copyUserUrl)}
                  {EmailCredentialRow("كلمة المرور المؤقتة", safePwd, copyPwdUrl, monospace: true)}
                  <table cellpadding="0" cellspacing="0" role="presentation" width="100%" style="margin-top:14px;">
                    <tr>
                      <td align="center">
                        <a href="{safeView}" target="_blank" rel="noopener noreferrer"
                           style="display:inline-block;background:#2563eb;color:#ffffff;font-size:14px;font-weight:600;padding:10px 24px;border-radius:6px;text-decoration:none;">عرض ونسخ بيانات الدخول</a>
                      </td>
                    </tr>
                  </table>
                </div>

                <p style="margin:0;font-size:12px;color:#888;line-height:1.5;">اضغط أيقونة النسخ الصغيرة أو الزر الأزرق لفتح صفحة النسخ (تعمل في Gmail). الرابط صالح 30 دقيقة.</p>
                <p style="margin:16px 0 0;font-size:11px;color:#aaa;">إذا لم تطلب إعادة التعيين، تواصل مع مدير المنصة.</p>
              </div>
            </body>
            </html>
            """;
    }

    private static string EmailCredentialRow(string label, string safeValue, string safeCopyUrl, bool monospace = false)
    {
        var font = monospace ? "Consolas,'Courier New',monospace" : "Tahoma,Arial,sans-serif";
        return $"""
            <table cellpadding="0" cellspacing="0" role="presentation" width="100%" style="margin-bottom:10px;">
              <tr>
                <td style="font-size:12px;color:#555;padding-bottom:5px;"><strong style="color:#333;">{label}</strong></td>
              </tr>
              <tr>
                <td>
                  <table cellpadding="0" cellspacing="0" role="presentation" width="100%" dir="ltr" style="background:#fff;border:1px solid #d4d4d4;border-radius:4px;">
                    <tr>
                      <td style="padding:8px 12px;font-size:13px;font-weight:600;text-align:left;color:#111;font-family:{font};-webkit-user-select:all;user-select:all;">{safeValue}</td>
                      <td width="34" align="center" valign="middle" style="border-left:1px solid #eee;padding:0;">
                        <a href="{safeCopyUrl}" target="_blank" rel="noopener noreferrer" title="نسخ"
                           style="display:block;padding:8px;text-decoration:none;line-height:0;">
                          {CopyIconHtml()}
                        </a>
                      </td>
                    </tr>
                  </table>
                </td>
              </tr>
            </table>
            """;
    }

    /// <summary>أيقونة نسخ صغيرة (CSS فقط) — الرابط يفتح صفحة النسخ في الموقع.</summary>
    private static string CopyIconHtml() => """
        <span style="display:inline-block;width:14px;height:14px;vertical-align:middle;position:relative;">
          <span style="position:absolute;top:0;right:0;width:9px;height:9px;border:1.5px solid #5c6bc0;border-radius:1px;background:#fff;"></span>
          <span style="position:absolute;bottom:0;left:0;width:9px;height:9px;border:1.5px solid #5c6bc0;border-radius:1px;background:#fff;"></span>
        </span>
        """;
}
