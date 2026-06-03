using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.FiscalYearManagement;

/// <summary>
/// أنماط تدوير الأرصدة الثلاثة:
///   • <c>WithProfitLoss</c>: يحسب صافي الربح/الخسارة من Revenue-Expense ويرحّله
///     لحساب الأرباح أو الخسارة. يُدوّر الميزانية (Asset/Liability/Equity).
///   • <c>BalanceSheetOnly</c>: يدوّر أرصدة الميزانية فقط (Asset/Liability/Equity)
///     كما هي بدون احتساب ربح/خسارة. مفيد للتدوير الجزئي.
///   • <c>AllAccounts</c>: يدوّر كل الحسابات بأرصدتها (الميزانية + الإيرادات +
///     المصروفات). يُستخدم عند الحاجة للحفاظ على أرصدة Revenue/Expense كأرصدة
///     افتتاحية في السنة الجديدة (سيناريو خاص).
/// </summary>
public enum RolloverMode
{
    WithProfitLoss = 1,
    BalanceSheetOnly = 2,
    AllAccounts = 3,
}

/// <summary>
/// بُعد العملة عند التدوير:
///   • <c>PerCurrency</c>: يُنشأ قيد افتتاحي مستقل لكل عملة بأرصدتها الأصلية
///     (دون تحويل) — يحافظ على الدقة التامة لأرصدة كل عملة.
///   • <c>ConvertToBase</c>: تُحوَّل كل العملات إلى العملة الأساسية وفق أحدث
///     نشرة منشورة، ويُنشأ قيد افتتاحي واحد بالعملة الأساسية.
/// </summary>
public enum RolloverCurrencyMode
{
    PerCurrency = 1,
    ConvertToBase = 2,
}

/// <summary>
/// أمر تدوير الأرصدة من سنة مالية مغلقة إلى سنة مالية لاحقة.
/// شروط:
///   • السنة المصدر يجب أن تكون مغلقة (يضمن ثبات الأرصدة).
///   • السنة الهدف يجب أن تكون لاحقة (StartDate &gt; EndDate المصدر).
///   • السنة الهدف يجب ألا تحوي قيداً افتتاحياً سابقاً.
/// </summary>
public record RolloverFiscalYearCommand(
    int SourceFiscalYearId,
    int TargetFiscalYearId,
    string PerformedBy,
    string? ProfitAccountCode,
    string? LossAccountCode,
    RolloverMode Mode = RolloverMode.WithProfitLoss,
    RolloverCurrencyMode CurrencyMode = RolloverCurrencyMode.PerCurrency,
    int? OpeningVoucherTypeId = null,
    bool RollBulletin = true,
    bool PreviewOnly = false,
    DateTime? OpeningEntryDate = null
) : IRequest<FiscalYearRolloverResultDto>;

public class RolloverFiscalYearHandler : IRequestHandler<RolloverFiscalYearCommand, FiscalYearRolloverResultDto>
{
    private readonly IAccountingDbContext _db;
    public RolloverFiscalYearHandler(IAccountingDbContext db) => _db = db;

    private const string OpeningLineDesc = "رصيد افتتاحي مُدوَّر";

    public async Task<FiscalYearRolloverResultDto> Handle(RolloverFiscalYearCommand req, CancellationToken ct)
    {
        // ─── 1) تحقّق السنوات ────────────────────────────────────────────────
        var src = await _db.FiscalYears
            .FirstOrDefaultAsync(f => f.Id == req.SourceFiscalYearId, ct)
            ?? throw new DomainException("السنة المصدر غير موجودة");
        var dst = await _db.FiscalYears
            .Include(f => f.Periods)
            .FirstOrDefaultAsync(f => f.Id == req.TargetFiscalYearId, ct)
            ?? throw new DomainException("السنة الهدف غير موجودة");

        if (!src.IsClosed)
            throw new DomainException(
                "لا يمكن تدوير الأرصدة من سنة مفتوحة. يجب إغلاق السنة المصدر أولاً " +
                "لضمان ثبات أرصدتها قبل التدوير.");

        if (dst.IsClosed)
            throw new DomainException("لا يمكن التدوير إلى سنة مالية مغلقة");

        if (dst.StartDate <= src.EndDate)
            throw new DomainException("السنة الهدف يجب أن تكون بعد السنة المصدر زمنياً");

        // ‎فحص وجود قيد افتتاحي سابق في السنة الهدف.
        var existingOpening = await _db.JournalEntries.AsNoTracking()
            .AnyAsync(e => e.FiscalYearId == dst.Id && e.EntryType == JournalEntryType.Opening, ct);
        if (existingOpening && !req.PreviewOnly)
            throw new DomainException(
                "يوجد قيد افتتاحي سابق في السنة الهدف. احذفه (تراجع عن التدوير) قبل المحاولة مجدداً.");

        // ─── 2) تحديد تاريخ القيد الافتتاحي والفترة المناسبة ──────────────────
        var openingDate = (req.OpeningEntryDate?.Date) ?? dst.StartDate.Date;
        if (openingDate < dst.StartDate.Date || openingDate > dst.EndDate.Date)
            throw new DomainException("تاريخ القيد الافتتاحي يجب أن يقع داخل السنة الهدف");

        var firstPeriod = dst.Periods.OrderBy(p => p.StartDate)
            .FirstOrDefault(p => p.StartDate.Date <= openingDate && p.EndDate.Date >= openingDate)
            ?? throw new DomainException("لا توجد فترة محاسبية تغطي تاريخ القيد الافتتاحي في السنة الهدف");

        // ─── 3) جمع أرصدة الحسابات الورقية من السنة المصدر مجمّعة حسب (حساب، عملة) ──
        // ‎الرصيد لكل (حساب، عملة) = Σ(Posted Debit - Posted Credit) خلال السنة المصدر.
        // ‎عملة السطر تساوي عملة قيده (لكل قيد عملة واحدة)، فالتجميع على e.Currency دقيق.
        var srcAgg = await (
            from l in _db.JournalEntryLines.AsNoTracking()
            join e in _db.JournalEntries.AsNoTracking() on l.JournalEntryId equals e.Id
            where e.FiscalYearId == src.Id && e.Status == JournalEntryStatus.Posted
            group new { l.IsDebit, l.Amount } by new { l.AccountId, e.Currency } into g
            select new
            {
                g.Key.AccountId,
                g.Key.Currency,
                Debit = g.Where(x => x.IsDebit).Sum(x => (decimal?)x.Amount) ?? 0m,
                Credit = g.Where(x => !x.IsDebit).Sum(x => (decimal?)x.Amount) ?? 0m,
            }).ToListAsync(ct);

        var accounts = await _db.Accounts.AsNoTracking()
            .Where(a => a.IsActive && a.IsLeaf)
            .ToListAsync(ct);
        var accById = accounts.ToDictionary(a => a.Id);

        // ‎العملة الأساسية ونشرة التحويل (تُستخدم في وضع ConvertToBase فقط).
        var asOf = openingDate.AddDays(1).AddTicks(-1);
        var convBulletin = await _db.CurrencyRateBulletins.AsNoTracking()
            .Include(b => b.Lines)
            .Where(b => b.Status == CurrencyRateBulletinStatus.Published && b.EffectiveAt <= asOf)
            .OrderByDescending(b => b.EffectiveAt).ThenByDescending(b => b.Id)
            .FirstOrDefaultAsync(ct);
        var baseCur = (convBulletin?.BaseCurrency ?? "IQD").Trim().ToUpperInvariant();
        var rates = new Dictionary<string, (decimal Rate, int Operation)>(StringComparer.OrdinalIgnoreCase);
        if (convBulletin != null)
            foreach (var line in convBulletin.Lines.Where(l => l.Rate > 0 && !string.IsNullOrWhiteSpace(l.Currency)))
                rates[line.Currency.Trim().ToUpperInvariant()] = (line.Rate, (int)line.Operation);

        // ‎صافي كل (عملة → (حساب → رصيد)). الرصيد موجب = مدين.
        var netByCurrency = new Dictionary<string, Dictionary<int, decimal>>(StringComparer.OrdinalIgnoreCase);
        void AddNet(string cur, int accId, decimal net)
        {
            cur = string.IsNullOrWhiteSpace(cur) ? baseCur : cur.Trim().ToUpperInvariant();
            if (!netByCurrency.TryGetValue(cur, out var map))
                netByCurrency[cur] = map = new Dictionary<int, decimal>();
            map[accId] = (map.TryGetValue(accId, out var prev) ? prev : 0m) + net;
        }

        foreach (var row in srcAgg)
        {
            if (!accById.ContainsKey(row.AccountId)) continue;
            AddNet(row.Currency, row.AccountId, row.Debit - row.Credit);
        }

        // ‎البذرة الأولى: إن لم يكن للسنة المصدر قيد افتتاحي (سنة أولى أُدخلت أرصدتها
        // ‎في OpeningBalance يدوياً) نضمّ OpeningBalance للعملة الأساسية. أما إذا كان
        // ‎لها قيد افتتاحي فأرصدتها مشمولة في الحركة أعلاه (لتفادي الازدواج).
        var srcHasOpening = await _db.JournalEntries.AsNoTracking()
            .AnyAsync(e => e.FiscalYearId == src.Id && e.EntryType == JournalEntryType.Opening, ct);
        if (!srcHasOpening)
        {
            foreach (var acc in accounts.Where(a => a.OpeningBalance != 0m))
            {
                var net = acc.Nature == AccountNature.Debit ? acc.OpeningBalance : -acc.OpeningBalance;
                AddNet(baseCur, acc.Id, net);
            }
        }

        // ─── 4) في وضع ConvertToBase: دمج كل العملات في العملة الأساسية ───────
        decimal MultiplierToBase(string cur)
        {
            var c = cur.Trim().ToUpperInvariant();
            if (c == baseCur) return 1m;
            if (rates.TryGetValue(c, out var r) && r.Rate > 0m)
                return r.Operation == 2 ? 1m / r.Rate : r.Rate;
            throw new DomainException(
                $"تعذّر التحويل: العملة {c} غير مُسعَّرة في نشرة منشورة سارية. " +
                "استخدم وضع 'قيد افتتاحي لكل عملة'، أو انشر نشرة أسعار تتضمّن هذه العملة.");
        }

        // قائمة السلال المراد إنشاء قيد لكل منها: (العملة، الحساب → الرصيد).
        List<(string Currency, Dictionary<int, decimal> Nets)> buckets;
        if (req.CurrencyMode == RolloverCurrencyMode.ConvertToBase)
        {
            var baseNets = new Dictionary<int, decimal>();
            foreach (var (cur, nets) in netByCurrency)
            {
                var mult = MultiplierToBase(cur);
                foreach (var kv in nets)
                    baseNets[kv.Key] = (baseNets.TryGetValue(kv.Key, out var prev) ? prev : 0m) + kv.Value * mult;
            }
            buckets = new() { (baseCur, baseNets) };
        }
        else
        {
            buckets = netByCurrency
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        // ─── 5) تصنيف رصيد سلّة (ميزانية / إيرادات-مصاريف) وحساب صافي الربح ────
        (List<(int Id, decimal Net)> Bs, List<(int Id, AccountType Type, decimal Net)> Pnl, decimal NetProfit)
            Classify(Dictionary<int, decimal> nets)
        {
            var bs = new List<(int, decimal)>();
            var pnl = new List<(int, AccountType, decimal)>();
            foreach (var kv in nets)
            {
                if (!accById.TryGetValue(kv.Key, out var a)) continue;
                var net = Math.Round(kv.Value, 3);
                if (net == 0m) continue;
                if (a.Type is AccountType.Asset or AccountType.Liability or AccountType.Equity)
                    bs.Add((a.Id, net));
                else
                    pnl.Add((a.Id, a.Type, net));
            }
            decimal totalRevenue = -pnl.Where(p => p.Item2 == AccountType.Revenue).Sum(p => p.Item3);
            decimal totalExpense = pnl.Where(p => p.Item2 == AccountType.Expense).Sum(p => p.Item3);
            return (bs, pnl, totalRevenue - totalExpense);
        }

        bool BucketHasContent(List<(int Id, decimal Net)> bs, List<(int Id, AccountType Type, decimal Net)> pnl, decimal netProfit)
            => req.Mode switch
            {
                RolloverMode.AllAccounts => bs.Count + pnl.Count > 0,
                RolloverMode.WithProfitLoss => bs.Count > 0 || Math.Round(netProfit, 3) != 0m,
                _ => bs.Count > 0,
            };

        string ModeLabel() => req.Mode switch
        {
            RolloverMode.WithProfitLoss => "مع إقفال الأرباح/الخسائر",
            RolloverMode.AllAccounts => "ترحيل كامل (شامل الإيرادات والمصاريف)",
            _ => "أرصدة الميزانية فقط",
        };

        // ─── 6) المعاينة: عدّ القيود والحسابات دون أي تعديل ──────────────────
        if (req.PreviewOnly)
        {
            int prevEntries = 0, prevBs = 0;
            decimal prevProfit = 0m;
            foreach (var (cur, nets) in buckets)
            {
                var (bs, pnl, netProfit) = Classify(nets);
                if (!BucketHasContent(bs, pnl, netProfit)) continue;
                prevEntries++;
                prevBs += bs.Count;
                prevProfit += netProfit;
            }
            var curLabel = req.CurrencyMode == RolloverCurrencyMode.PerCurrency
                ? $"{prevEntries} قيد افتتاحي (قيد لكل عملة)"
                : $"قيد افتتاحي واحد بالعملة الأساسية {baseCur}";
            return new FiscalYearRolloverResultDto
            {
                Success = true,
                FromFiscalYearId = src.Id,
                ToFiscalYearId = dst.Id,
                BalanceSheetAccountsRolled = prevBs,
                RetainedEarningsTransferred = req.Mode == RolloverMode.WithProfitLoss ? prevProfit : 0m,
                OpeningEntriesCreated = prevEntries,
                Message = $"معاينة: سيتم إنشاء {curLabel} — نمط: {ModeLabel()}.",
            };
        }

        // ─── 7) جلب حسابات الربح/الخسارة (لوضع WithProfitLoss) ────────────────
        Account? profitAcc = null, lossAcc = null;
        if (req.Mode == RolloverMode.WithProfitLoss)
        {
            if (string.IsNullOrWhiteSpace(req.ProfitAccountCode))
                throw new DomainException("كود حساب الأرباح مطلوب في وضع 'إقفال مع الربح/الخسارة'");
            if (string.IsNullOrWhiteSpace(req.LossAccountCode))
                throw new DomainException("كود حساب الخسائر مطلوب في وضع 'إقفال مع الربح/الخسارة'");

            profitAcc = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Code == req.ProfitAccountCode && a.IsLeaf && a.IsActive, ct)
                ?? throw new DomainException($"حساب الأرباح بالكود {req.ProfitAccountCode} غير موجود أو غير نشط أو ليس ورقياً");
            lossAcc = await _db.Accounts
                .FirstOrDefaultAsync(a => a.Code == req.LossAccountCode && a.IsLeaf && a.IsActive, ct)
                ?? throw new DomainException($"حساب الخسائر بالكود {req.LossAccountCode} غير موجود أو غير نشط أو ليس ورقياً");
        }

        // ‎نوع السند الافتتاحي (اختياري): يُستخدم فقط إن اختاره المستخدم صراحةً.
        // ‎لا يوجد إسناد تلقائي لأي نوع سند: تركه فارغاً يجعل القيد الافتتاحي قيداً
        // ‎يدوياً عادياً قابلاً للتعديل بالكامل من نافذة القيود اليومية — تماماً كأي
        // ‎قيد افتتاحي يُدخله المستخدم بنفسه.
        // ‎وعند اختيار نوع سند، يجب أن يكون من النوع المختلط (Mixed) لأن القيد
        // ‎الافتتاحي قيد متعدد البنود (مدين/دائن) — والأنواع غير المختلطة تُمنع من
        // ‎التعديل في نافذة القيود اليومية فيصبح القيد غير قابل للتحرير.
        JournalVoucherType? openingVt = null;
        if (req.OpeningVoucherTypeId.HasValue)
        {
            openingVt = await _db.JournalVoucherTypes
                .FirstOrDefaultAsync(v => v.Id == req.OpeningVoucherTypeId.Value, ct)
                ?? throw new DomainException("نوع السند الافتتاحي المختار غير موجود");
            if (!openingVt.IsEnabled)
                throw new DomainException($"نوع السند الافتتاحي '{openingVt.NameAr}' معطّل");
            if (openingVt.Nature != VoucherNature.Mixed)
                throw new DomainException(
                    $"نوع السند الافتتاحي '{openingVt.NameAr}' ليس من النوع المختلط (Mixed). " +
                    "اختر نوع سند مختلطاً ليبقى القيد الافتتاحي قابلاً للتعديل، أو اتركه فارغاً.");
        }

        // ─── 8) تنفيذ التدوير ضمن معاملة ─────────────────────────────────────
        await using var trx = await _db.BeginTransactionAsync(ct);

        int entriesCreated = 0, bsRolled = 0;
        decimal retained = 0m;

        foreach (var (cur, nets) in buckets)
        {
            var (bs, pnl, netProfit) = Classify(nets);
            if (!BucketHasContent(bs, pnl, netProfit)) continue;

            var rolling = req.Mode == RolloverMode.AllAccounts
                ? bs.Concat(pnl.Select(p => (p.Id, p.Net))).ToList()
                : bs;

            var lines = new List<(int AccountId, bool IsDebit, decimal Amount, string? Description)>();
            decimal totalDebit = 0m, totalCredit = 0m;
            foreach (var (accId, net) in rolling)
            {
                if (net > 0m) { lines.Add((accId, true, net, OpeningLineDesc)); totalDebit += net; }
                else { lines.Add((accId, false, -net, OpeningLineDesc)); totalCredit += -net; }
            }

            if (req.Mode == RolloverMode.WithProfitLoss && Math.Round(netProfit, 3) != 0m)
            {
                if (netProfit > 0m)
                {
                    lines.Add((profitAcc!.Id, false, netProfit, "صافي الربح المرحَّل من السنة السابقة"));
                    totalCredit += netProfit;
                }
                else
                {
                    lines.Add((lossAcc!.Id, true, -netProfit, "صافي الخسارة المرحَّلة من السنة السابقة"));
                    totalDebit += -netProfit;
                }
            }

            if (lines.Count == 0) continue;

            if (Math.Round(totalDebit - totalCredit, 3) != 0m)
                throw new DomainException(
                    $"القيد الافتتاحي للعملة {cur} غير متوازن. مدين={totalDebit:N3}، دائن={totalCredit:N3}، " +
                    $"فرق={(totalDebit - totalCredit):N3}. في وضع 'الميزانية فقط' قد تحتاج تضمين الأرباح/الخسائر.");

            var entryNum = await _db.GetNextJournalEntryNumberAsync(dst.Id, ct);
            int? voucherSeq = openingVt != null
                ? await _db.GetNextVoucherSequenceAsync(openingVt.Id, dst.Id, ct)
                : null;

            var entry = JournalEntry.Create(
                date: openingDate,
                fyId: dst.Id,
                periodId: firstPeriod.Id,
                source: JournalEntrySource.Manual,
                description: $"قيد افتتاحي مُدوَّر من {src.Name} [{cur}] ({ModeLabel()})",
                type: JournalEntryType.Opening,
                currency: cur,
                entryNumber: entryNum.ToString(),
                voucherTypeId: openingVt?.Id,
                voucherSequence: voucherSeq);

            entry.ReplaceLines(lines);
            // ‎قيد افتتاحي كمسودة — يُراجع ويُعدَّل ويُرحَّل يدوياً مثل أي قيد يُنشأ من الواجهة.
            await _db.JournalEntries.AddAsync(entry, ct);

            entriesCreated++;
            bsRolled += bs.Count;
            retained += netProfit;
        }

        if (entriesCreated == 0)
            throw new DomainException("لا توجد أرصدة قابلة للتدوير إلى السنة الهدف");

        // ─── 9) تدوير نشرة الأسعار المعتمدة إلى السنة الجديدة (اختياري) ───────
        int? rolledBulletinId = null;
        if (req.RollBulletin)
        {
            var srcAsOf = src.EndDate.Date.AddDays(1).AddTicks(-1);
            var srcBulletin = await _db.CurrencyRateBulletins.AsNoTracking()
                .Include(b => b.Lines)
                .Where(b => b.Status == CurrencyRateBulletinStatus.Published && b.EffectiveAt <= srcAsOf)
                .OrderByDescending(b => b.EffectiveAt).ThenByDescending(b => b.Id)
                .FirstOrDefaultAsync(ct);

            // لا نُكرّر التدوير إن وُجدت نشرة منشورة سارية بالفعل عند بداية السنة الهدف.
            var alreadyHasBulletin = await _db.CurrencyRateBulletins.AsNoTracking()
                .AnyAsync(b => b.Status == CurrencyRateBulletinStatus.Published
                            && b.EffectiveAt <= asOf
                            && b.EffectiveAt >= dst.StartDate.Date, ct);

            if (srcBulletin != null && srcBulletin.Lines.Count > 0 && !alreadyHasBulletin)
            {
                var clone = CurrencyRateBulletin.Create(
                    name: $"نشرة مُدوَّرة — {dst.Name}",
                    baseCurrency: srcBulletin.BaseCurrency,
                    effectiveAt: dst.StartDate.Date,
                    notes: $"مُدوَّرة تلقائياً من نشرة '{srcBulletin.Name}' عند تدوير {src.Name} ⇒ {dst.Name}");
                foreach (var line in srcBulletin.Lines)
                    clone.AddLine(line.Currency, line.Rate, line.Operation, line.Notes);
                clone.Publish(req.PerformedBy);
                await _db.CurrencyRateBulletins.AddAsync(clone, ct);
                await _db.SaveChangesAsync(ct);
                rolledBulletinId = clone.Id;
            }
        }

        await _db.SaveChangesAsync(ct);
        await trx.CommitAsync(ct);

        var currencyNote = req.CurrencyMode == RolloverCurrencyMode.PerCurrency
            ? $"{entriesCreated} قيد افتتاحي (قيد لكل عملة)"
            : $"قيد افتتاحي واحد بالعملة الأساسية {baseCur}";
        var bulletinNote = rolledBulletinId.HasValue ? " وتم تدوير نشرة الأسعار." : "";

        return new FiscalYearRolloverResultDto
        {
            Success = true,
            FromFiscalYearId = src.Id,
            ToFiscalYearId = dst.Id,
            BalanceSheetAccountsRolled = bsRolled,
            RetainedEarningsTransferred = req.Mode == RolloverMode.WithProfitLoss ? retained : 0m,
            OpeningEntriesCreated = entriesCreated,
            RolledBulletinId = rolledBulletinId,
            Message = $"تم إنشاء {currencyNote} كمسودة — راجعها وعدّلها ثم رحّلها يدوياً. نمط: {ModeLabel()}.{bulletinNote}",
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────
// أمر التراجع عن التدوير: يحذف القيد الافتتاحي في السنة الهدف، يُعيد ضبط
// OpeningBalance للحسابات المتأثّرة، ويفك إغلاق السنة المصدر تلقائياً
// (اختيارياً) ليتمكّن المستخدم من تعديل قيود السنة السابقة.
// ─────────────────────────────────────────────────────────────────────────
public record UndoRolloverCommand(
    int TargetFiscalYearId,
    bool ReopenSource = true
) : IRequest<UndoRolloverResultDto>;

public class UndoRolloverResultDto
{
    public bool Success { get; set; }
    public int DeletedEntryId { get; set; }
    public int AffectedAccounts { get; set; }
    public int? ReopenedSourceId { get; set; }
    public string Message { get; set; } = default!;
}

public class UndoRolloverHandler : IRequestHandler<UndoRolloverCommand, UndoRolloverResultDto>
{
    private readonly IAccountingDbContext _db;
    public UndoRolloverHandler(IAccountingDbContext db) => _db = db;

    public async Task<UndoRolloverResultDto> Handle(UndoRolloverCommand req, CancellationToken ct)
    {
        var dst = await _db.FiscalYears
            .FirstOrDefaultAsync(f => f.Id == req.TargetFiscalYearId, ct)
            ?? throw new DomainException("السنة الهدف غير موجودة");

        if (dst.IsClosed)
            throw new DomainException("لا يمكن التراجع عن التدوير بعد إغلاق السنة الهدف. افكّ إغلاقها أولاً.");

        // ‎كل القيود الافتتاحية في السنة الهدف (قد تكون عدّة قيود — قيد لكل عملة).
        var openings = await _db.JournalEntries
            .Include(e => e.Lines)
            .Where(e => e.FiscalYearId == dst.Id && e.EntryType == JournalEntryType.Opening)
            .ToListAsync(ct);
        if (openings.Count == 0)
            throw new DomainException("لا يوجد قيد افتتاحي في السنة الهدف لإلغائه");

        var affectedAccountIds = openings
            .SelectMany(o => o.Lines.Select(l => l.AccountId))
            .Distinct()
            .ToList();

        await using var trx = await _db.BeginTransactionAsync(ct);

        // ‎حذف كل القيود الافتتاحية وأسطرها.
        foreach (var opening in openings)
        {
            _db.JournalEntryLines.RemoveRange(opening.Lines);
            _db.JournalEntries.Remove(opening);
        }

        // ‎تصفير OpeningBalance للحسابات المتأثّرة.
        var accs = await _db.Accounts
            .Where(a => affectedAccountIds.Contains(a.Id))
            .ToListAsync(ct);
        foreach (var a in accs) a.SetOpeningBalance(0m);

        await _db.SaveChangesAsync(ct);

        // ‎فك إغلاق السنة المصدر تلقائياً (إذا طُلب).
        int? reopenedId = null;
        if (req.ReopenSource)
        {
            var src = await _db.FiscalYears
                .Include(f => f.Periods)
                .Where(f => f.EndDate < dst.StartDate)
                .OrderByDescending(f => f.EndDate)
                .FirstOrDefaultAsync(ct);
            if (src != null && src.IsClosed)
            {
                src.Reopen();
                reopenedId = src.Id;
                await _db.SaveChangesAsync(ct);
            }
        }

        await trx.CommitAsync(ct);

        var deletedFirst = openings[0].Id;
        return new UndoRolloverResultDto
        {
            Success = true,
            DeletedEntryId = deletedFirst,
            AffectedAccounts = affectedAccountIds.Count,
            ReopenedSourceId = reopenedId,
            Message = reopenedId.HasValue
                ? $"تم حذف {openings.Count} قيد افتتاحي وتصفير {affectedAccountIds.Count} حساب وفك إغلاق السنة السابقة"
                : $"تم حذف {openings.Count} قيد افتتاحي وتصفير {affectedAccountIds.Count} حساب",
        };
    }
}
