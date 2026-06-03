using System;

namespace IraqiTradeCenterCompany.API.Settings;

/// <summary>
/// إعدادات أرشيف الميديا والنسخ الاحتياطي (Singleton Id=1).
/// المستخدم يحدد <see cref="MediaRootPath"/> — تحتها تُنشأ مجلدات لكل سنة
/// مالية وبداخلها ملفات منفصلة لكل نافذة (RV/PV/JV/JE) + قاعدة البيانات.
/// </summary>
public class MediaBackupSettings
{
    public int Id { get; set; } = 1;

    /// <summary>المسار الجذري الذي يحدده المستخدم (قرص الخادم).</summary>
    public string? MediaRootPath { get; set; }

    public bool IncludeDatabaseBackup { get; set; } = true;
    /// <summary>بعد إنشاء .bak محلياً، رفعها إلى Cloudflare R2 (نفس إعدادات أرشيف المرفقات).</summary>
    public bool SyncDatabaseBackupToR2 { get; set; }
    /// <summary>عدد نسخ .bak المحفوظة على السيرفر لكل سنة (الأقدم يُحذف). 0 = بدون حد.</summary>
    public int ServerDatabaseBackupKeepCount { get; set; } = 3;
    /// <summary>عدد نسخ .bak المحفوظة على R2 لكل سنة (الأقدم يُحذف). 0 = بدون حد.</summary>
    public int R2DatabaseBackupKeepCount { get; set; } = 10;
    public bool IncludeVoucherData { get; set; } = true;
    public bool IncludeAttachments { get; set; } = true;

    public bool AutoBackupEnabled { get; set; }
    /// <summary>تعبير cron بسيط أو وصف جدولة — للمرحلة اللاحقة.</summary>
    public string? AutoBackupCron { get; set; }

    public int RetentionYears { get; set; } = 5;

    public DateTime? LastRunAtUtc { get; set; }
    /// <summary>Idle | Running | Success | Failed</summary>
    public string LastRunStatus { get; set; } = "Idle";
    public string? LastRunError { get; set; }
    public string? LastRunYearFolder { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
