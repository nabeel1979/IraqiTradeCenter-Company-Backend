using System.Threading;
using System.Threading.Tasks;

namespace IraqiTradeCenterCompany.SharedKernel.Interfaces;

/// <summary>
/// حذف مرفقات السند/القيد (soft-delete + تنظيف المحلي/R2).
/// </summary>
public interface IVoucherAttachmentDeletionService
{
    /// <summary>حذف جميع المرفقات غير المحذوفة لقيد محاسبي. يُرجع العدد المحذوف.</summary>
    Task<int> DeleteAllForJournalEntryAsync(int journalEntryId, CancellationToken ct = default);
}
