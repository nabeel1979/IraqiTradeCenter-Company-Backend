namespace IraqiTradeCenterCompany.API.Settings;

/// <summary>
/// إعدادات SMTP للبريد (Zoho Mail افتراضياً) — سطر واحد Id=1.
/// تُستخدم لإرسال ردود الفواتير الإلكترونية ورسائل الاختبار.
/// </summary>
public class EmailSettings
{
    public int Id { get; set; } = 1;

    public bool IsEnabled { get; set; }

    /// <summary>Zoho | Custom</summary>
    public string Provider { get; set; } = "Zoho";

    public string SmtpHost { get; set; } = "smtp.zoho.com";

    public int SmtpPort { get; set; } = 587;

    /// <summary>StartTls | Ssl</summary>
    public string SecurityMode { get; set; } = "StartTls";

    /// <summary>عادةً البريد الكامل (مثل info@company.iq).</summary>
    public string? Username { get; set; }

    /// <summary>كلمة مرور التطبيق من Zoho (App Password) — لا تُعاد للواجهة.</summary>
    public string? AppPassword { get; set; }

    public string? FromEmail { get; set; }

    public string? FromDisplayName { get; set; }

    public string? ReplyToEmail { get; set; }

    public string? SignatureHtml { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }
}
