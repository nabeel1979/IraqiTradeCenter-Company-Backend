using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// حسابات الإدارة المالية (نوع طرف أو طرف فردي) — لا تُعدَّل ولا تُحذف من شجرة الحسابات.
/// </summary>
internal static class FinancialManagementAccountGuard
{
    public const string ManagedAccountMessage =
        "هذا الحساب مُدار من الإدارة المالية — عدّله أو احذفه من نافذة الإدارة المالية فقط.";

    public const string ManagedParentMessage =
        "لا يمكن إضافة حسابات تحت حساب مُدار من الإدارة المالية — أضف الأطراف من نافذة الإدارة المالية.";

    public static async Task<bool> IsManagedAccountAsync(
        IAccountingDbContext db, int accountId, CancellationToken ct)
    {
        if (await db.FinancialParties.AsNoTracking()
                .AnyAsync(p => p.AccountId == accountId, ct))
            return true;

        return await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.IsLockedForParties)
            .FirstOrDefaultAsync(ct);
    }
}
