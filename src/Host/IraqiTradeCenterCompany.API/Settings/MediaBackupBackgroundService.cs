using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IraqiTradeCenterCompany.API.Settings;

/// <summary>
/// يُشغّل النسخ الاحتياطي تلقائياً حسب <see cref="MediaBackupSettings.AutoBackupCron"/>.
/// يعمل كل دقيقة ويستخدم السنة المالية النشطة.
/// </summary>
public class MediaBackupBackgroundService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly HashSet<string> _firedSlotKeys = new(StringComparer.Ordinal);

    private readonly IServiceProvider _services;
    private readonly ILogger<MediaBackupBackgroundService> _log;

    public static MediaBackupSchedulerStatus Status { get; } = new();

    public MediaBackupBackgroundService(IServiceProvider services, ILogger<MediaBackupBackgroundService> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("MediaBackupBackgroundService started — tick {Tick}", TickInterval);

        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await TickAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Media backup scheduler tick failed");
                Status.RecordError(ex.Message);
            }

            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        Status.LastTickAtUtc = DateTime.UtcNow;

        using var scope = _services.CreateScope();
        var settingsSvc = scope.ServiceProvider.GetRequiredService<IMediaBackupSettingsService>();
        var runner = scope.ServiceProvider.GetRequiredService<IMediaBackupRunner>();
        var db = scope.ServiceProvider.GetRequiredService<IAccountingDbContext>();

        var settings = await settingsSvc.GetAsync(ct);
        Status.AutoBackupEnabled = settings.AutoBackupEnabled;
        Status.AutoBackupCron = settings.AutoBackupCron;
        Status.NextRunAtUtc = settings.AutoBackupEnabled
            ? MediaBackupScheduleHelper.GetNextOccurrenceUtc(settings.AutoBackupCron, DateTime.UtcNow)
            : null;

        if (!settings.AutoBackupEnabled) return;
        if (!settings.IncludeDatabaseBackup)
        {
            Status.LastWarning = "الجدولة مفعّلة لكن نسخة قاعدة البيانات غير مفعّلة.";
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.MediaRootPath))
        {
            Status.LastWarning = "الجدولة مفعّلة لكن مسار الأرشيف غير محدّد.";
            return;
        }
        if (!MediaBackupScheduleHelper.TryParseAll(settings.AutoBackupCron, out _, out var parseErr))
        {
            Status.LastWarning = parseErr;
            return;
        }

        Status.LastWarning = null;
        if (!MediaBackupScheduleHelper.IsDueNow(settings.AutoBackupCron, DateTime.UtcNow, _firedSlotKeys))
            return;

        if (runner.IsRunning)
        {
            _log.LogInformation("Scheduled media backup skipped — manual/another run in progress");
            return;
        }

        if (!await _gate.WaitAsync(0, ct)) return;
        try
        {
            var slotKey = MediaBackupScheduleHelper.GetCurrentSlotKey(settings.AutoBackupCron, DateTime.UtcNow, _firedSlotKeys);
            if (slotKey == null)
                return;

            var fy = await db.FiscalYears.AsNoTracking()
                .Where(f => f.IsActive)
                .OrderByDescending(f => f.Id)
                .FirstOrDefaultAsync(ct)
                ?? await db.FiscalYears.AsNoTracking()
                    .OrderByDescending(f => f.Id)
                    .FirstOrDefaultAsync(ct);

            if (fy == null)
            {
                Status.LastWarning = "لا توجد سنة مالية — تخطّي الجدولة.";
                return;
            }

            _firedSlotKeys.Add(slotKey);
            PruneOldFiredSlots();

            _log.LogInformation("Starting scheduled media backup for fiscal year {FyId} ({FyName})", fy.Id, fy.Name);
            var result = await runner.RunAsync(
                new MediaBackupRunRequest { FiscalYearId = fy.Id },
                "جدولة تلقائية",
                ct);

            Status.LastScheduledRunAtUtc = DateTime.UtcNow;
            Status.LastScheduledSuccess = result.Success;
            Status.LastScheduledMessage = result.Message;

            _log.LogInformation("Scheduled media backup finished: success={Ok}, db={Db}, r2={R2}",
                result.Success, result.DatabaseFile, result.DatabaseSyncedToR2);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Scheduled media backup failed");
            Status.RecordError(ex.Message);
            Status.LastScheduledRunAtUtc = DateTime.UtcNow;
            Status.LastScheduledSuccess = false;
            Status.LastScheduledMessage = ex.Message;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void PruneOldFiredSlots()
    {
        if (_firedSlotKeys.Count <= 48) return;
        // يُبقي المفاتيح الحديثة فقط — كافٍ لعدة أيام × 12 موعداً
        var keep = _firedSlotKeys.OrderByDescending(x => x, StringComparer.Ordinal).Take(48).ToHashSet(StringComparer.Ordinal);
        _firedSlotKeys.Clear();
        foreach (var k in keep) _firedSlotKeys.Add(k);
    }
}

public class MediaBackupSchedulerStatus
{
    public DateTime? LastTickAtUtc { get; set; }
    public bool AutoBackupEnabled { get; set; }
    public string? AutoBackupCron { get; set; }
    public DateTime? NextRunAtUtc { get; set; }
    public DateTime? LastScheduledRunAtUtc { get; set; }
    public bool? LastScheduledSuccess { get; set; }
    public string? LastScheduledMessage { get; set; }
    public string? LastWarning { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAtUtc { get; set; }

    public void RecordError(string err)
    {
        LastError = err.Length > 500 ? err[..500] : err;
        LastErrorAtUtc = DateTime.UtcNow;
    }
}
