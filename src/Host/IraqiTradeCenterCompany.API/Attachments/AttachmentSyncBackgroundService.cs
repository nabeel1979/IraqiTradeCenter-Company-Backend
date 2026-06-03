using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IraqiTradeCenterCompany.API.Settings;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IraqiTradeCenterCompany.API.Attachments;

/// <summary>
/// خدمة خلفية تُشغَّل ضمن الـ App Pool لمزامنة المرفقات بين القرص المحلي و
/// Cloudflare R2. تعمل بدورة كل دقيقة (configurable) وتُنجز ثلاث مهام:
///
/// <list type="number">
///   <item><b>دفع الرفع</b> (Upload→R2): تأخذ كل صف
///     <see cref="AttachmentSyncOperation.Upload"/> + <see cref="AttachmentSyncStatus.Pending"/>،
///     تقرأ الملف من القرص المحلي، وتدفعه إلى R2 بنفس الـ <c>StorageKey</c>،
///     ثم تضبط <c>SyncedToR2AtUtc=now</c> و
///     <c>LocalPurgeAfterUtc=now+24h</c> وتؤشر المرفق <c>IsOnR2=true</c>.</item>
///
///   <item><b>تنفيذ الحذف</b> (Delete→R2): تأخذ الـ tombstones وتحذف الكائن
///     من R2. الحذف <i>idempotent</i> — لو لم يكن موجوداً تُعتبر العملية ناجحة.</item>
///
///   <item><b>تنظيف المحلي</b>: تأخذ صفوف الرفع المُتزامنة التي مرّ عليها
///     ≥24 ساعة وتحذف الملف من القرص المحلي، ثم تؤشر المرفق
///     <c>IsOnLocal=false</c>.</item>
/// </list>
///
/// <para><b>أمان متعدّد العمّال</b>: نعتمد على دورة قصيرة (60s) + قفل
/// <c>SemaphoreSlim</c> داخل العملية الواحدة. لو شُغّل التطبيق على عدّة
/// خوادم سيلزم لاحقاً قفل توزيعي (مثل sp_getapplock أو SQL row-lock عبر
/// <c>SELECT … WITH (UPDLOCK, READPAST)</c>) — حالياً مع App Pool واحد
/// هذا كافٍ.</para>
/// </summary>
public class AttachmentSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<AttachmentSyncBackgroundService> _log;

    /// <summary>دورة العمل — كل دقيقة. ثابتة لأن المتطلّب نصّ عليها صراحةً.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <summary>مدّة بقاء النسخة المحلّية بعد المزامنة (24 ساعة).</summary>
    private static readonly TimeSpan LocalRetention = TimeSpan.FromHours(24);

    /// <summary>عدد الصفوف القصوى لمعالجتها في كل دورة (تجنّب dead-lock طويل).</summary>
    private const int BatchSize = 50;

    /// <summary>أقصى عدد محاولات قبل تأشير الصف Failed (يعاد لاحقاً يدوياً).</summary>
    private const int MaxAttempts = 5;

    /// <summary>قفل لمنع تشغيل دورتين متوازيتين (cron tick + manual trigger مثلاً).</summary>
    private static readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>إحصائيات في الذاكرة لاستعمال نقطة /sync-status.</summary>
    public static SyncStatusSnapshot Status { get; } = new();

    public AttachmentSyncBackgroundService(
        IServiceProvider services,
        ILogger<AttachmentSyncBackgroundService> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("AttachmentSyncBackgroundService started — tick {Tick}, retention {Retention}",
            TickInterval, LocalRetention);

        // ‎انتظار قصير عند الإقلاع كي لا نضرب قاعدة البيانات قبل اكتمال الـ migrations.
        try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(TickInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogError(ex, "Attachment sync tick failed unexpectedly");
                Status.RecordError(ex.Message);
            }

            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>دورة عمل واحدة — قابلة للاستدعاء يدوياً عبر زر "مزامنة الآن" مستقبلاً.</summary>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct))
        {
            _log.LogDebug("Attachment sync tick skipped (already running)");
            return;
        }
        try
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IAccountingDbContext>();
            var registry = scope.ServiceProvider.GetRequiredService<IAttachmentStorageRegistry>();
            var settings = scope.ServiceProvider.GetRequiredService<IAttachmentSettingsService>();

            // ‎نتأكّد من توفّر إعدادات R2 — وإلا فلا فائدة من المحاولة (نُسجّل تحذيراً
            // ‎مرّة واحدة كل دورة كي لا تمتلئ السجلات).
            var row = await settings.GetAsync(ct);
            var r2Configured = !string.IsNullOrWhiteSpace(row.R2AccountId)
                && !string.IsNullOrWhiteSpace(row.R2AccessKeyId)
                && !string.IsNullOrWhiteSpace(row.R2SecretAccessKey)
                && !string.IsNullOrWhiteSpace(row.R2Bucket);

            if (!r2Configured)
            {
                Status.LastWarning = "R2 settings are incomplete — skipping push/delete this tick.";
                Status.LastTickAtUtc = DateTime.UtcNow;
                await CountStatusAsync(db, ct);
                return;
            }
            Status.LastWarning = null;

            var local = registry.GetByName("Local");
            var r2 = registry.GetByName("R2");

            int uploaded = 0, deleted = 0, purged = 0;

            // (1) رفع الـ pending uploads إلى R2.
            var pendingUploads = await db.AttachmentSyncOutbox
                .Where(o => o.Operation == AttachmentSyncOperation.Upload
                            && o.Status == AttachmentSyncStatus.Pending)
                .OrderBy(o => o.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            foreach (var op in pendingUploads)
            {
                if (ct.IsCancellationRequested) break;
                op.MarkAttempt();
                try
                {
                    await using var stream = await local.OpenReadAsync(op.StorageKey, ct);
                    var r2Impl = r2 as R2AttachmentStorage
                        ?? throw new InvalidOperationException("R2 implementation type mismatch");
                    await r2Impl.UploadWithKeyAsync(op.StorageKey, stream, op.ContentType, ct);

                    op.MarkUploadedToR2(DateTime.UtcNow, LocalRetention);

                    var att = await db.VoucherAttachments.FirstOrDefaultAsync(a => a.Id == op.AttachmentId, ct);
                    if (att != null) att.MarkUploadedToR2();
                    uploaded++;
                }
                catch (FileNotFoundException ex)
                {
                    // ‎الملف المحلي غير موجود — قد يكون حُذف يدوياً. نعتبر العملية مُنجزة (لا شيء لرفعه).
                    _log.LogWarning(ex, "Local file missing for outbox upload {Id} — marking as failed/cancelled", op.Id);
                    op.MarkFailed("Local file missing");
                }
                catch (Exception ex) when (op.Attempts >= MaxAttempts)
                {
                    _log.LogError(ex, "Upload to R2 failed permanently after {Attempts} attempts (outbox {Id})", op.Attempts, op.Id);
                    op.MarkFailed(ex.Message);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Upload to R2 attempt {Attempts} failed (outbox {Id}) — will retry", op.Attempts, op.Id);
                    op.MarkFailed(ex.Message);
                    op.Requeue(); // ‎يبقى Pending لمحاولة الدورة التالية
                }
            }
            if (pendingUploads.Count > 0) await db.SaveChangesAsync(ct);

            // (2) تنفيذ الـ tombstones (حذف من R2).
            var pendingDeletes = await db.AttachmentSyncOutbox
                .Where(o => o.Operation == AttachmentSyncOperation.Delete
                            && o.Status == AttachmentSyncStatus.Pending)
                .OrderBy(o => o.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            foreach (var op in pendingDeletes)
            {
                if (ct.IsCancellationRequested) break;
                op.MarkAttempt();
                try
                {
                    await r2.DeleteAsync(op.StorageKey, ct);
                    op.MarkDeletedFromR2();
                    deleted++;
                }
                catch (Exception ex) when (op.Attempts >= MaxAttempts)
                {
                    _log.LogError(ex, "Delete from R2 failed permanently (outbox {Id})", op.Id);
                    op.MarkFailed(ex.Message);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Delete from R2 attempt {Attempts} failed (outbox {Id}) — will retry", op.Attempts, op.Id);
                    op.MarkFailed(ex.Message);
                    op.Requeue();
                }
            }
            if (pendingDeletes.Count > 0) await db.SaveChangesAsync(ct);

            // (3) تنظيف النسخة المحلية للملفات التي مرّ على رفعها لـ R2 ≥ 24 ساعة.
            var now = DateTime.UtcNow;
            var toPurge = await db.AttachmentSyncOutbox
                .Where(o => o.Operation == AttachmentSyncOperation.Upload
                            && o.Status == AttachmentSyncStatus.Synced
                            && o.LocalPurgeAfterUtc != null
                            && o.LocalPurgeAfterUtc <= now)
                .OrderBy(o => o.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            foreach (var op in toPurge)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    await local.DeleteAsync(op.StorageKey, ct);
                    op.MarkLocalPurged();
                    var att = await db.VoucherAttachments.FirstOrDefaultAsync(a => a.Id == op.AttachmentId, ct);
                    if (att != null) att.MarkLocalPurged();
                    purged++;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Local purge failed for outbox {Id} — will retry next tick", op.Id);
                    // ‎نُبقيها Synced كي تُلتقط في الدورة التالية تلقائياً.
                }
            }
            if (toPurge.Count > 0) await db.SaveChangesAsync(ct);

            // ‎حدّث snapshot الإحصائيات.
            Status.LastTickAtUtc = DateTime.UtcNow;
            Status.LastUploadedCount = uploaded;
            Status.LastDeletedCount = deleted;
            Status.LastLocalPurgedCount = purged;
            await CountStatusAsync(db, ct);

            if (uploaded + deleted + purged > 0)
            {
                _log.LogInformation("Attachment sync tick: uploaded={Up}, deleted={Del}, purgedLocal={Purge}",
                    uploaded, deleted, purged);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task CountStatusAsync(IAccountingDbContext db, CancellationToken ct)
    {
        Status.PendingUploads = await db.AttachmentSyncOutbox
            .CountAsync(o => o.Operation == AttachmentSyncOperation.Upload
                          && o.Status == AttachmentSyncStatus.Pending, ct);
        Status.PendingDeletes = await db.AttachmentSyncOutbox
            .CountAsync(o => o.Operation == AttachmentSyncOperation.Delete
                          && o.Status == AttachmentSyncStatus.Pending, ct);
        Status.FailedCount = await db.AttachmentSyncOutbox
            .CountAsync(o => o.Status == AttachmentSyncStatus.Failed, ct);
        Status.PendingLocalPurge = await db.AttachmentSyncOutbox
            .CountAsync(o => o.Operation == AttachmentSyncOperation.Upload
                          && o.Status == AttachmentSyncStatus.Synced
                          && o.LocalPurgeAfterUtc != null, ct);
    }
}

/// <summary>
/// لقطة الإحصائيات الحالية لمزامنة المرفقات. مشتركة بين السيرفس و الـ controller
/// عبر static — لا حاجة لمخزن مشترك خارجي. تُحدَّث في كل دورة.
/// </summary>
public class SyncStatusSnapshot
{
    public DateTime? LastTickAtUtc { get; set; }
    public int PendingUploads { get; set; }
    public int PendingDeletes { get; set; }
    public int FailedCount { get; set; }
    public int PendingLocalPurge { get; set; }

    public int LastUploadedCount { get; set; }
    public int LastDeletedCount { get; set; }
    public int LastLocalPurgedCount { get; set; }

    public string? LastWarning { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastErrorAtUtc { get; set; }

    public void RecordError(string err)
    {
        LastError = err.Length > 500 ? err[..500] : err;
        LastErrorAtUtc = DateTime.UtcNow;
    }
}
