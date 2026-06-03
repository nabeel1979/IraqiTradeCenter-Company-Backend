using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// قاعدة حماية صلاحيات العملة للأطراف المالية (مورد/عميل/مصرف):
///   إذا كان أحد حسابات القيد مرتبطاً بطرف مالي له قائمة عملات مسموحة محدَّدة،
///   فيجب أن تكون عملة السند ضمن تلك القائمة — وإلا يُرفض الحفظ/التعديل.
///
/// السلوك متساهل تجاه الأطراف التي لم تُحدَّد لها عملات (قائمة فارغة) — لا قيد عليها،
/// لتفادي كسر الأطراف القديمة. القيد يُطبَّق فقط عندما تكون هناك قائمة عملات صريحة.
///
/// تُستدعى من جميع handlers الإدخال/التحديث: PostJournalEntry، UpdateVoucherEntry،
/// UpdateJournalEntry — كي لا يُلتف على القاعدة من أي مسار.
/// </summary>
internal static class FinancialPartyGuard
{
    public static async Task<string?> ValidateAsync(
        IAccountingDbContext db,
        IReadOnlyList<int> lineAccountIds,
        string currency,
        CancellationToken ct)
    {
        var accountIds = lineAccountIds.Distinct().ToList();
        if (accountIds.Count == 0) return null;

        var parties = await db.FinancialParties
            .AsNoTracking()
            .Include(p => p.Account)
            .Where(p => accountIds.Contains(p.AccountId))
            .ToListAsync(ct);

        if (parties.Count == 0) return null;

        var cur = (currency ?? "IQD").Trim().ToUpperInvariant();

        foreach (var party in parties)
        {
            var allowed = party.GetAllowedCurrenciesList();
            // ‎لا قيد إذا لم تُحدَّد عملات للطرف.
            if (allowed.Count == 0) continue;

            var has = allowed.Any(c => string.Equals(c, cur, StringComparison.OrdinalIgnoreCase));
            if (!has)
            {
                var name = party.Account?.NameAr ?? $"#{party.AccountId}";
                var list = string.Join("، ", allowed);
                return $"الحساب '{name}' لا يملك صلاحية العملة {cur} — العملات المسموحة: {list}. غيّر عملة السند أو أضف العملة إلى بطاقة الطرف.";
            }
        }

        return null;
    }
}
