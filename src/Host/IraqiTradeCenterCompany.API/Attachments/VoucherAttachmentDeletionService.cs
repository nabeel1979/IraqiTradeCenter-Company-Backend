using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.API.Attachments;

/// <summary>
/// ينفّذ حذف مرفقات السند: soft-delete + إزالة من القرص/R2 (عبر outbox).
/// </summary>
public class VoucherAttachmentDeletionService : IVoucherAttachmentDeletionService
{
    private readonly IAccountingDbContext _db;
    private readonly IAttachmentStorageRegistry _storageRegistry;

    public VoucherAttachmentDeletionService(
        IAccountingDbContext db,
        IAttachmentStorageRegistry storageRegistry)
    {
        _db = db;
        _storageRegistry = storageRegistry;
    }

    public async Task<int> DeleteAllForJournalEntryAsync(int journalEntryId, CancellationToken ct = default)
    {
        var attachments = await _db.VoucherAttachments
            .Where(a => a.JournalEntryId == journalEntryId && !a.IsDeleted)
            .ToListAsync(ct);

        if (attachments.Count == 0) return 0;

        foreach (var att in attachments)
            await PurgeAttachmentStorageAsync(att, ct);

        await _db.SaveChangesAsync(ct);
        return attachments.Count;
    }

    /// <summary>يُستدعى أيضاً من <see cref="Controllers.VoucherAttachmentsController"/> لحذف مرفق واحد.</summary>
    internal async Task PurgeSingleAsync(VoucherAttachment att, CancellationToken ct)
    {
        await PurgeAttachmentStorageAsync(att, ct);
        await _db.SaveChangesAsync(ct);
    }

    private async Task PurgeAttachmentStorageAsync(VoucherAttachment att, CancellationToken ct)
    {
        if (att.IsOnLocal)
        {
            try
            {
                var local = _storageRegistry.GetByName("Local");
                await local.DeleteAsync(att.StorageKey, ct);
                att.MarkLocalPurged();
            }
            catch { /* best effort */ }
        }

        if (att.IsOnR2)
        {
            _db.AttachmentSyncOutbox.Add(AttachmentSyncOutbox.CreateDelete(att.Id, att.StorageKey));
        }
        else
        {
            var pending = await _db.AttachmentSyncOutbox
                .Where(o => o.AttachmentId == att.Id
                    && o.Operation == AttachmentSyncOperation.Upload
                    && o.Status == AttachmentSyncStatus.Pending)
                .ToListAsync(ct);
            foreach (var p in pending) p.MarkLocalPurged();
        }

        att.MarkAsDeleted();
    }
}
