using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// قواعد حماية حسابات الصناديق وحسابات الوسيط:
///   1. الحسابات المرتبطة بصندوق لا يجوز تحريكها عبر قيد عام (JV) — فقط عبر سندات
///      قبض/دفع المسجَّلة على نوع سند فيه VoucherTypeId.
///   2. حسابات الوسيط (تسوية الحسابات) لا يجوز تحريكها يدوياً — فقط عبر تسوية/مناقلة.
///   3. السندات التي تحرّك صندوقاً يجب أن تحترم سقف المدين/الدائن المعرَّف لذلك
///      الصندوق بتلك العملة (إن وُجد).
/// </summary>
internal static class CashBoxGuard
{
    /// <summary>تمثيل خفيف لسطر قيد (يقبل سطور موجودة أو سطور جديدة).</summary>
    public readonly record struct LineSnapshot(int AccountId, bool IsDebit, decimal Amount);

    /// <summary>
    /// يرجِع رسالة خطأ إذا كان هناك حساب صندوق في القيد دون VoucherTypeId،
    /// أو إذا تخطّى أحد السطور سقف الصندوق. يرجِع null إذا كان كل شيء سليماً.
    /// </summary>
    /// <param name="db">سياق قاعدة البيانات.</param>
    /// <param name="lines">سطور القيد الجديدة/المعدَّلة.</param>
    /// <param name="currency">عملة القيد.</param>
    /// <param name="voucherTypeId">معرّف نوع السند — null للقيد العام.</param>
    /// <param name="excludeJournalEntryId">عند التحديث: استثناء قيد قائم من حساب الرصيد الحالي.</param>
    /// <param name="allowTransitAccounts">true للعمليات النظامية (تسوية/مناقلة) التي تحرّك الوسيط.</param>
    public static async Task<string?> ValidateAsync(
        IAccountingDbContext db,
        IReadOnlyList<LineSnapshot> lines,
        string currency,
        int? voucherTypeId,
        int? excludeJournalEntryId,
        CancellationToken ct,
        bool allowTransitAccounts = false)
    {
        var lineAccountIds = lines.Select(l => l.AccountId).Distinct().ToList();
        if (lineAccountIds.Count == 0) return null;

        if (!allowTransitAccounts)
        {
            var linkedIds = await AccountSettlementLinkedSource.GetAllLinkedAccountIdsAsync(db, ct);
            var linkedHit = lineAccountIds.FirstOrDefault(linkedIds.Contains);
            if (linkedHit != 0)
            {
                var tName = await db.Accounts.AsNoTracking()
                    .Where(a => a.Id == linkedHit)
                    .Select(a => a.NameAr)
                    .FirstOrDefaultAsync(ct);
                return $"الحساب '{tName ?? linkedHit.ToString()}' مرتبط بإعدادات تسوية الحسابات — لا يمكن تحريكه يدوياً في القيود. يُستخدم تلقائياً عبر تسوية الحسابات.";
            }
        }

        var cashBoxes = await CashBoxPartySource.GetByAccountIdsAsync(db, lineAccountIds, ct);
        if (cashBoxes.Count == 0) return null;

        var boxByAccount = cashBoxes.ToDictionary(b => b.AccountId);

        // (1) لا يجوز تحريك حساب صندوق إلا عبر سند (VoucherTypeId مطلوب)
        if (!voucherTypeId.HasValue)
        {
            var firstBox = cashBoxes[0];
            return $"الحساب '{firstBox.NameAr}' مرتبط بصندوق ({firstBox.NameAr}) ولا يمكن تحريكه عبر قيد عام — استخدم سند قبض أو سند دفع.";
        }

        // (2) فحص السقوف لكل سطر يلامس صندوقاً
        var cur = (currency ?? "IQD").Trim().ToUpperInvariant();

        foreach (var accountId in lineAccountIds.Where(boxByAccount.ContainsKey))
        {
            var box = boxByAccount[accountId];

            decimal delta = 0m;
            foreach (var l in lines.Where(x => x.AccountId == box.AccountId))
                delta += l.IsDebit ? l.Amount : -l.Amount;

            var ledger = from l in db.JournalEntryLines.AsNoTracking()
                         join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                         where l.AccountId == box.AccountId
                            && e.Currency == cur
                            && e.Status == JournalEntryStatus.Posted
                            && !l.IsDeleted
                            && !e.IsDeleted
                         select new { l.IsDebit, l.Amount, l.JournalEntryId };

            if (excludeJournalEntryId.HasValue)
            {
                var excludeId = excludeJournalEntryId.Value;
                ledger = ledger.Where(x => x.JournalEntryId != excludeId);
            }

            var currentDebit = await ledger.Where(x => x.IsDebit).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            var currentCredit = await ledger.Where(x => !x.IsDebit).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            var currentBalance = currentDebit - currentCredit;
            var newBalance = currentBalance + delta;

            var cbCur = CashBoxPartySource.GetCurrency(box, cur);
            if (cbCur == null)
                return $"الصندوق '{box.NameAr}' لا يدعم العملة {cur} — أضف العملة إلى الصندوق في الإدارة المالية أو غيّر عملة السند.";

            if (cbCur.DebitLimit.HasValue && newBalance > cbCur.DebitLimit.Value)
            {
                return $"تم تجاوز السقف المدين للصندوق '{box.NameAr}' ({cur}): الرصيد الناتج {Format(newBalance)} > السقف {Format(cbCur.DebitLimit.Value)}.";
            }

            if (cbCur.CreditLimit.HasValue && newBalance < -cbCur.CreditLimit.Value)
            {
                return $"تم تجاوز السقف الدائن للصندوق '{box.NameAr}' ({cur}): الرصيد الناتج {Format(newBalance)} < السقف -{Format(cbCur.CreditLimit.Value)}.";
            }
        }

        return null;
    }

    /// <summary>
    /// يتحقق من أن أرصدة الصناديق بعد استبعاد قيود محددة (كأنها محذوفة) لا تتجاوز السقوف.
    /// يُستخدم قبل الحذف النهائي لتسوية مُلغاة.
    /// </summary>
    public static async Task<string?> ValidateAfterExcludingEntriesAsync(
        IAccountingDbContext db,
        IReadOnlyList<int> excludeJournalEntryIds,
        CancellationToken ct)
    {
        if (excludeJournalEntryIds.Count == 0) return null;

        var excludedLines = await (
            from l in db.JournalEntryLines.AsNoTracking()
            join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where excludeJournalEntryIds.Contains(e.Id)
               && e.Status == JournalEntryStatus.Posted
               && !l.IsDeleted
               && !e.IsDeleted
            select new { l.AccountId, e.Currency, l.IsDebit, l.Amount }
        ).ToListAsync(ct);

        if (excludedLines.Count == 0) return null;

        var accountIds = excludedLines.Select(x => x.AccountId).Distinct().ToList();
        var cashBoxes = await CashBoxPartySource.GetByAccountIdsAsync(db, accountIds, ct);
        if (cashBoxes.Count == 0) return null;

        var boxByAccount = cashBoxes.ToDictionary(b => b.AccountId);

        foreach (var grp in excludedLines.GroupBy(x => (
            x.AccountId,
            Cur: (x.Currency ?? "IQD").Trim().ToUpperInvariant())))
        {
            if (!boxByAccount.TryGetValue(grp.Key.AccountId, out var box)) continue;

            decimal removedDelta = 0m;
            foreach (var l in grp)
                removedDelta += l.IsDebit ? l.Amount : -l.Amount;

            var ledger = from l in db.JournalEntryLines.AsNoTracking()
                         join e in db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
                         where l.AccountId == grp.Key.AccountId
                            && e.Currency == grp.Key.Cur
                            && e.Status == JournalEntryStatus.Posted
                            && !l.IsDeleted
                            && !e.IsDeleted
                         select new { l.IsDebit, l.Amount };

            var currentDebit = await ledger.Where(x => x.IsDebit).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            var currentCredit = await ledger.Where(x => !x.IsDebit).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            var futureBalance = (currentDebit - currentCredit) - removedDelta;

            var cbCur = CashBoxPartySource.GetCurrency(box, grp.Key.Cur);
            if (cbCur == null)
                return $"الصندوق '{box.NameAr}' لا يدعم العملة {grp.Key.Cur}.";

            if (cbCur.DebitLimit.HasValue && futureBalance > cbCur.DebitLimit.Value)
            {
                return $"لا يمكن حذف التسوية: الرصيد الناتج للصندوق '{box.NameAr}' ({grp.Key.Cur}) {Format(futureBalance)} يتجاوز السقف المدين {Format(cbCur.DebitLimit.Value)}.";
            }

            if (cbCur.CreditLimit.HasValue && futureBalance < -cbCur.CreditLimit.Value)
            {
                return $"لا يمكن حذف التسوية: الرصيد الناتج للصندوق '{box.NameAr}' ({grp.Key.Cur}) {Format(futureBalance)} يتجاوز السقف الدائن -{Format(cbCur.CreditLimit.Value)}.";
            }
        }

        return null;
    }

    private static string Format(decimal v) =>
        v.ToString("N3", System.Globalization.CultureInfo.InvariantCulture);
}
