using IraqiTradeCenterCompany.SharedKernel.Common;

namespace IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;

/// <summary>
/// طابور مزامنة المرفقات: يدير نقل الملفات بين القرص المحلي للخادم و
/// Cloudflare R2. كل سطر يُمثّل عملية واحدة (رفع أو حذف) قابلة للتأجيل
/// والإعادة عند الفشل دون أن تُعطّل عمليات المستخدم اللحظية.
///
/// <para><b>تدفّق الرفع</b>: عند رفع المستخدم لمرفق جديد ⇒ يُحفظ على القرص
/// محلياً فوراً (سرعة)، ويُسجّل صف <see cref="AttachmentSyncOperation.Upload"/>
/// هنا. الـ <c>BackgroundService</c> يقرأ كل دقيقة، يدفع الملف إلى R2،
/// ويضبط <see cref="LocalPurgeAfterUtc"/> بعد 24 ساعة. عند انتهاء المدّة
/// يحذف الملف من القرص ويبقى متاحاً من R2 وحده.</para>
///
/// <para><b>تدفّق الحذف</b>: عند طلب الحذف ⇒ يُحذف محلياً فوراً + يُسجّل
/// صف <see cref="AttachmentSyncOperation.Delete"/>. السيرفس يحذفه من R2
/// بعدها (tombstone propagation).</para>
/// </summary>
public class AttachmentSyncOutbox : BaseEntity
{
    /// <summary>FK إلى المرفق (قد يبقى السطر بعد حذف المرفق ناعماً).</summary>
    public int AttachmentId { get; private set; }

    /// <summary>نوع العملية: رفع أو حذف.</summary>
    public AttachmentSyncOperation Operation { get; private set; }

    /// <summary>المفتاح/المسار الواحد المشترك (نفسه في القرص المحلي وفي R2).</summary>
    public string StorageKey { get; private set; } = default!;

    /// <summary>نوع المحتوى — يُستعمل عند الرفع لـ R2 (ContentType).</summary>
    public string? ContentType { get; private set; }

    public long SizeBytes { get; private set; }

    public AttachmentSyncStatus Status { get; private set; } = AttachmentSyncStatus.Pending;

    /// <summary>عدّاد المحاولات — يُزاد عند كل فشل ليساعد في exponential backoff.</summary>
    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public DateTime? LastAttemptAtUtc { get; private set; }

    /// <summary>وقت اكتمال الرفع لـ R2 (Upload فقط).</summary>
    public DateTime? SyncedToR2AtUtc { get; private set; }

    /// <summary>بعد هذا الوقت يحذف السيرفس النسخة المحلّية (Upload فقط — اعتماد 24س).</summary>
    public DateTime? LocalPurgeAfterUtc { get; private set; }

    public DateTime? LocalPurgedAtUtc { get; private set; }

    private AttachmentSyncOutbox() { }

    public static AttachmentSyncOutbox CreateUpload(int attachmentId, string storageKey, string? contentType, long sizeBytes)
        => new()
        {
            AttachmentId = attachmentId,
            Operation = AttachmentSyncOperation.Upload,
            StorageKey = storageKey,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Status = AttachmentSyncStatus.Pending,
        };

    public static AttachmentSyncOutbox CreateDelete(int attachmentId, string storageKey)
        => new()
        {
            AttachmentId = attachmentId,
            Operation = AttachmentSyncOperation.Delete,
            StorageKey = storageKey,
            Status = AttachmentSyncStatus.Pending,
        };

    /// <summary>تأشير العملية كـ "قيد المعالجة الآن" — يُستخدم لمنع التكرار بين الـ ticks.</summary>
    public void MarkAttempt()
    {
        Attempts++;
        LastAttemptAtUtc = DateTime.UtcNow;
        LastError = null;
    }

    public void MarkUploadedToR2(DateTime nowUtc, TimeSpan localRetention)
    {
        Status = AttachmentSyncStatus.Synced;
        SyncedToR2AtUtc = nowUtc;
        LocalPurgeAfterUtc = nowUtc.Add(localRetention);
    }

    public void MarkDeletedFromR2()
    {
        Status = AttachmentSyncStatus.Synced;
        SyncedToR2AtUtc = DateTime.UtcNow;
    }

    public void MarkLocalPurged()
    {
        Status = AttachmentSyncStatus.PurgedLocal;
        LocalPurgedAtUtc = DateTime.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = AttachmentSyncStatus.Failed;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }

    /// <summary>إعادة تأهيل سطر فشل سابقاً (للمحاولة من جديد).</summary>
    public void Requeue()
    {
        Status = AttachmentSyncStatus.Pending;
    }
}

public enum AttachmentSyncOperation
{
    Upload = 1,
    Delete = 2,
}

public enum AttachmentSyncStatus
{
    /// <summary>منتظر للمعالجة.</summary>
    Pending = 0,

    /// <summary>تمّ بنجاح (Upload: مرفوع لـ R2 وينتظر مسح المحلي / Delete: حُذف من R2).</summary>
    Synced = 1,

    /// <summary>فشل بعد عدد محاولات (يُعاد تشغيله يدوياً أو تلقائياً عبر backoff).</summary>
    Failed = 2,

    /// <summary>(Upload فقط) — تمّ مسح النسخة المحلّية بعد المهلة.</summary>
    PurgedLocal = 3,
}
