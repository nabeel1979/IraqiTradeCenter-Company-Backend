using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IraqiTradeCenterCompany.API.Attachments;
using IraqiTradeCenterCompany.API.Auth.Permissions;
using IraqiTradeCenterCompany.API.Controllers;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace IraqiTradeCenterCompany.API.Settings;

[Route("api/settings/media-backup")]
public class MediaBackupController : BaseApiController
{
    private readonly IMediaBackupSettingsService _settings;
    private readonly IMediaBackupRunner _runner;
    private readonly IAttachmentStorageRegistry _storage;
    private readonly ICurrentUserService _currentUser;
    private readonly IPermissionService _perms;
    private readonly IAuditLogger _audit;

    public MediaBackupController(
        IMediaBackupSettingsService settings,
        IMediaBackupRunner runner,
        IAttachmentStorageRegistry storage,
        ICurrentUserService currentUser,
        IPermissionService perms,
        IAuditLogger audit)
    {
        _settings = settings;
        _runner = runner;
        _storage = storage;
        _currentUser = currentUser;
        _perms = perms;
        _audit = audit;
    }

    public class MediaBackupSettingsDto
    {
        public string? MediaRootPath { get; set; }
        public bool IncludeDatabaseBackup { get; set; }
        public bool SyncDatabaseBackupToR2 { get; set; }
        public int ServerDatabaseBackupKeepCount { get; set; }
        public int R2DatabaseBackupKeepCount { get; set; }
        public bool IncludeVoucherData { get; set; }
        public bool IncludeAttachments { get; set; }
        public bool AutoBackupEnabled { get; set; }
        public string? AutoBackupCron { get; set; }
        public string? AutoBackupScheduleDescription { get; set; }
        public string? NextAutoBackupAtUtc { get; set; }
        public string? LastScheduledRunAtUtc { get; set; }
        public int RetentionYears { get; set; }
        public string LastRunStatus { get; set; } = "Idle";
        public string? LastRunError { get; set; }
        public string? LastRunAtUtc { get; set; }
        public string? LastRunYearFolder { get; set; }
        public bool IsRunning { get; set; }
        public string? UpdatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class UpdateMediaBackupSettingsRequest
    {
        public string? MediaRootPath { get; set; }
        public bool? IncludeDatabaseBackup { get; set; }
        public bool? SyncDatabaseBackupToR2 { get; set; }
        public int? ServerDatabaseBackupKeepCount { get; set; }
        public int? R2DatabaseBackupKeepCount { get; set; }
        public bool? IncludeVoucherData { get; set; }
        public bool? IncludeAttachments { get; set; }
        public bool? AutoBackupEnabled { get; set; }
        public string? AutoBackupCron { get; set; }
        public int? RetentionYears { get; set; }
    }

    public class TestPathRequest
    {
        public string? MediaRootPath { get; set; }
    }

    public class RunMediaBackupRequest
    {
        public int FiscalYearId { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!await CanReadAsync(ct)) return Forbid();
        var row = await _settings.GetAsync(ct);
        return Ok(new { success = true, data = ToDto(row) });
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdateMediaBackupSettingsRequest req, CancellationToken ct)
    {
        if (!await CanUpdateAsync(ct)) return Forbid();

        string? pathWarning = null;
        if (!string.IsNullOrWhiteSpace(req.MediaRootPath))
        {
            var (ok, msg) = MediaBackupSettingsService.TestPath(req.MediaRootPath);
            if (!ok) return BadRequest(new { success = false, message = msg });
            pathWarning = msg;
        }

        var user = _currentUser.FullName ?? _currentUser.UserId?.ToString();

        // تطبيق التعديلات في الذاكرة أولاً للتحقق من الجدولة قبل الحفظ.
        var preview = await _settings.GetAsync(ct);
        var nextEnabled = req.AutoBackupEnabled ?? preview.AutoBackupEnabled;
        var nextCron = req.AutoBackupCron != null
            ? (string.IsNullOrWhiteSpace(req.AutoBackupCron) ? null : req.AutoBackupCron.Trim())
            : preview.AutoBackupCron;
        var nextIncludeDb = req.IncludeDatabaseBackup ?? preview.IncludeDatabaseBackup;
        var nextPath = req.MediaRootPath != null
            ? (string.IsNullOrWhiteSpace(req.MediaRootPath) ? null : req.MediaRootPath.Trim())
            : preview.MediaRootPath;

        if (nextEnabled)
        {
            if (!nextIncludeDb)
                return BadRequest(new { success = false, message = "فعّل نسخة قاعدة البيانات لتشغيل الجدولة التلقائية." });
            if (string.IsNullOrWhiteSpace(nextPath))
                return BadRequest(new { success = false, message = "حدّد مسار الأرشيف قبل تفعيل الجدولة." });
            if (string.IsNullOrWhiteSpace(nextCron))
                nextCron = MediaBackupScheduleHelper.DefaultCron;
            if (!MediaBackupScheduleHelper.TryParseAll(nextCron, out _, out var cronErr))
                return BadRequest(new { success = false, message = cronErr });
        }

        var row = await _settings.UpdateAsync(s =>
        {
            if (req.MediaRootPath != null) s.MediaRootPath = string.IsNullOrWhiteSpace(req.MediaRootPath) ? null : req.MediaRootPath.Trim();
            if (req.IncludeDatabaseBackup.HasValue) s.IncludeDatabaseBackup = req.IncludeDatabaseBackup.Value;
            if (req.SyncDatabaseBackupToR2.HasValue) s.SyncDatabaseBackupToR2 = req.SyncDatabaseBackupToR2.Value;
            if (req.ServerDatabaseBackupKeepCount.HasValue) s.ServerDatabaseBackupKeepCount = Math.Clamp(req.ServerDatabaseBackupKeepCount.Value, 0, 100);
            if (req.R2DatabaseBackupKeepCount.HasValue) s.R2DatabaseBackupKeepCount = Math.Clamp(req.R2DatabaseBackupKeepCount.Value, 0, 100);
            if (req.IncludeVoucherData.HasValue) s.IncludeVoucherData = req.IncludeVoucherData.Value;
            if (req.IncludeAttachments.HasValue) s.IncludeAttachments = req.IncludeAttachments.Value;
            if (req.AutoBackupEnabled.HasValue) s.AutoBackupEnabled = req.AutoBackupEnabled.Value;
            if (req.AutoBackupCron != null || (req.AutoBackupEnabled == true && string.IsNullOrWhiteSpace(s.AutoBackupCron)))
                s.AutoBackupCron = nextCron;
            if (req.RetentionYears.HasValue) s.RetentionYears = Math.Clamp(req.RetentionYears.Value, 1, 50);
        }, user, ct);

        await _audit.LogAsync("MediaBackupSettings", "1", AuditActions.Update,
            "تحديث إعدادات أرشيف الميديا والنسخ الاحتياطي",
            new { row.MediaRootPath, row.IncludeDatabaseBackup, row.IncludeAttachments }, ct);

        return Ok(new { success = true, data = ToDto(row), warning = pathWarning });
    }

    [HttpPost("test-path")]
    public async Task<IActionResult> TestPath([FromBody] TestPathRequest req, CancellationToken ct)
    {
        if (!await CanUpdateAsync(ct)) return Forbid();
        var path = req.MediaRootPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var row = await _settings.GetAsync(ct);
            path = row.MediaRootPath;
        }
        var (ok, msg) = MediaBackupSettingsService.TestPath(path);
        return Ok(new { success = ok, message = msg });
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run([FromBody] RunMediaBackupRequest req, CancellationToken ct)
    {
        if (!await CanRunAsync(ct)) return Forbid();
        if (req.FiscalYearId <= 0)
            return BadRequest(new { success = false, message = "اختر سنة مالية." });

        if (_runner.IsRunning)
            return Conflict(new { success = false, message = "عملية أرشفة أخرى قيد التشغيل." });

        var user = _currentUser.FullName ?? _currentUser.UserId?.ToString();
        try
        {
            var result = await _runner.RunAsync(new MediaBackupRunRequest { FiscalYearId = req.FiscalYearId }, user, ct);
            await _audit.LogAsync("MediaBackup", req.FiscalYearId.ToString(), AuditActions.Create,
                $"إنشاء أرشيف ميديا للسنة {result.YearFolder}",
                new { result.YearFolder, result.TotalSizeBytes, result.DatabaseFile }, ct);
            return Ok(new { success = true, data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>قائمة نسخ قاعدة البيانات (.bak) المتوفرة تحت مسار الأرشيف.</summary>
    [HttpGet("database-files")]
    public async Task<IActionResult> ListDatabaseFiles(CancellationToken ct)
    {
        if (!await CanReadAsync(ct)) return Forbid();
        var row = await _settings.GetAsync(ct);
        var files = MediaBackupCatalog.ListDatabaseBackups(row.MediaRootPath);
        return Ok(new
        {
            success = true,
            data = files.Select(f => new
            {
                yearFolder = f.YearFolder,
                fileName = f.FileName,
                sizeBytes = f.SizeBytes,
                createdAtUtc = f.CreatedAtUtc.ToString("o"),
            }),
        });
    }

    /// <summary>تنزيل نسخة .bak محددة (yearFolder + fileName).</summary>
    [HttpGet("database-files/download")]
    public async Task<IActionResult> DownloadDatabaseFile(
        [FromQuery] string yearFolder,
        [FromQuery] string fileName,
        CancellationToken ct)
    {
        if (!await CanReadAsync(ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(yearFolder) || string.IsNullOrWhiteSpace(fileName))
            return BadRequest(new { success = false, message = "حدّد السنة واسم الملف." });

        var row = await _settings.GetAsync(ct);
        var fullPath = MediaBackupCatalog.ResolveDatabaseBackupPath(row.MediaRootPath, yearFolder.Trim(), fileName.Trim());
        if (fullPath == null)
            return NotFound(new { success = false, message = "الملف غير موجود." });

        await _audit.LogAsync("MediaBackup", $"{yearFolder}/{fileName}", AuditActions.View,
            $"تنزيل نسخة قاعدة البيانات {fileName}",
            new { yearFolder, fileName }, ct);

        return PhysicalFile(fullPath, "application/octet-stream", fileDownloadName: Path.GetFileName(fullPath), enableRangeProcessing: true);
    }

    /// <summary>قائمة نسخ قاعدة البيانات (.bak) المخزّنة على Cloudflare R2.</summary>
    [HttpGet("r2-database-files")]
    public async Task<IActionResult> ListR2DatabaseFiles(CancellationToken ct)
    {
        if (!await CanReadAsync(ct)) return Forbid();
        var row = await _settings.GetAsync(ct);
        if (!row.SyncDatabaseBackupToR2)
            return Ok(new { success = true, data = Array.Empty<object>() });

        var r2 = _storage.GetByName("R2") as R2AttachmentStorage;
        if (r2 == null)
            return BadRequest(new { success = false, message = "R2 غير مهيّأ — أكمل إعدادات Cloudflare R2 من قسم أرشيف المرفقات." });

        try
        {
            var files = await MediaBackupR2Catalog.ListDatabaseBackupsAsync(r2, ct);
            return Ok(new
            {
                success = true,
                data = files.Select(f => new
                {
                    r2Key = f.R2Key,
                    yearFolder = f.YearFolder,
                    fileName = f.FileName,
                    sizeBytes = f.SizeBytes,
                    createdAtUtc = f.CreatedAtUtc.ToString("o"),
                }),
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    /// <summary>تطبيق سياسة الاحتفاظ على R2 فوراً (حذف النسخ الزائدة).</summary>
    [HttpPost("r2-database-files/apply-retention")]
    public async Task<IActionResult> ApplyR2Retention(CancellationToken ct)
    {
        if (!await CanUpdateAsync(ct)) return Forbid();
        var row = await _settings.GetAsync(ct);
        if (!row.SyncDatabaseBackupToR2)
            return BadRequest(new { success = false, message = "مزامنة R2 غير مفعّلة." });

        var r2 = _storage.GetByName("R2") as R2AttachmentStorage;
        if (r2 == null)
            return BadRequest(new { success = false, message = "R2 غير مهيّأ — أكمل إعدادات Cloudflare R2 من قسم أرشيف المرفقات." });

        try
        {
            var purged = await MediaBackupR2Catalog.ApplyRetentionAsync(r2, row.R2DatabaseBackupKeepCount, ct: ct);
            await _audit.LogAsync("MediaBackup", "R2", AuditActions.Update,
                $"تطبيق سياسة الاحتفاظ على R2 — حُذفت {purged} نسخة",
                new { row.R2DatabaseBackupKeepCount, purged }, ct);
            return Ok(new { success = true, purgedCount = purged });
        }
        catch (Exception ex)
        {
            return BadRequest(new { success = false, message = ex.Message });
        }
    }

    private MediaBackupSettingsDto ToDto(MediaBackupSettings row)
    {
        string? scheduleDesc = null;
        if (MediaBackupScheduleHelper.TryParseAll(row.AutoBackupCron, out var parsed, out _))
            scheduleDesc = MediaBackupScheduleHelper.DescribeAll(parsed);

        var nextUtc = row.AutoBackupEnabled
            ? MediaBackupScheduleHelper.GetNextOccurrenceUtc(row.AutoBackupCron, DateTime.UtcNow)
            : null;

        return new MediaBackupSettingsDto
        {
            MediaRootPath = row.MediaRootPath,
            IncludeDatabaseBackup = row.IncludeDatabaseBackup,
            SyncDatabaseBackupToR2 = row.SyncDatabaseBackupToR2,
            ServerDatabaseBackupKeepCount = row.ServerDatabaseBackupKeepCount,
            R2DatabaseBackupKeepCount = row.R2DatabaseBackupKeepCount,
            IncludeVoucherData = row.IncludeVoucherData,
            IncludeAttachments = row.IncludeAttachments,
            AutoBackupEnabled = row.AutoBackupEnabled,
            AutoBackupCron = row.AutoBackupCron,
            AutoBackupScheduleDescription = scheduleDesc,
            NextAutoBackupAtUtc = nextUtc?.ToString("o"),
            LastScheduledRunAtUtc = MediaBackupBackgroundService.Status.LastScheduledRunAtUtc?.ToString("o"),
            RetentionYears = row.RetentionYears,
            LastRunStatus = row.LastRunStatus,
            LastRunError = row.LastRunError,
            LastRunAtUtc = row.LastRunAtUtc?.ToString("o"),
            LastRunYearFolder = row.LastRunYearFolder,
            IsRunning = _runner.IsRunning,
            UpdatedAtUtc = row.UpdatedAtUtc?.ToString("o"),
            UpdatedBy = row.UpdatedBy,
        };
    }

    private async Task<bool> CanReadAsync(CancellationToken ct) =>
        await HasPerm(PermissionRegistry.System.MediaBackup.Read, ct)
        || await HasPerm(PermissionRegistry.System.CompanySettings.Read, ct);

    private async Task<bool> CanUpdateAsync(CancellationToken ct) =>
        await HasPerm(PermissionRegistry.System.MediaBackup.Update, ct)
        || await HasPerm(PermissionRegistry.System.CompanySettings.Update, ct);

    private async Task<bool> CanRunAsync(CancellationToken ct) =>
        await HasPerm(PermissionRegistry.System.MediaBackup.Run, ct)
        || await HasPerm(PermissionRegistry.System.CompanySettings.Update, ct);

    private async Task<bool> HasPerm(string code, CancellationToken ct)
    {
        var uid = _currentUser.UserId;
        return uid.HasValue && await _perms.HasPermissionAsync(uid.Value, code, ct);
    }
}
