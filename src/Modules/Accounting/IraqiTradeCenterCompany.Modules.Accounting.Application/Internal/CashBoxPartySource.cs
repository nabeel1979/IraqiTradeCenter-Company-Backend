using IraqiTradeCenterCompany.Modules.Accounting.Application.Features.CashBoxes;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// مصدر موحّد للصناديق التشغيلية — يقرأ من أطراف الإدارة المالية (Kind = CashBox)
/// بدلاً من جدول acc.CashBoxes القديم.
/// </summary>
internal static class CashBoxPartySource
{
    public sealed record CurrencyView(
        string Currency,
        decimal? DebitLimit,
        decimal? CreditLimit,
        bool IsActive);

    public sealed record PartyView(
        int Id,
        string Code,
        string NameAr,
        string? NameEn,
        int AccountId,
        bool IsActive,
        int DisplayOrder,
        IReadOnlyList<CurrencyView> Currencies);

    public static async Task<List<PartyView>> GetAllAsync(
        IAccountingDbContext db,
        bool? activeOnly,
        CancellationToken ct)
    {
        var q = BaseQuery(db);
        if (activeOnly == true)
            q = q.Where(p => p.IsActive);

        var rows = await q
            .OrderBy(p => p.Category!.DisplayOrder)
            .ThenBy(p => p.Account!.Code)
            .ToListAsync(ct);

        return rows.Select(MapParty).ToList();
    }

    public static async Task<PartyView?> GetByIdAsync(
        IAccountingDbContext db,
        int id,
        CancellationToken ct)
    {
        var row = await BaseQuery(db).FirstOrDefaultAsync(p => p.Id == id, ct);
        return row == null ? null : MapParty(row);
    }

    public static async Task<Dictionary<int, PartyView>> GetByIdsAsync(
        IAccountingDbContext db,
        IEnumerable<int> ids,
        CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<int, PartyView>();

        var rows = await BaseQuery(db)
            .Where(p => idList.Contains(p.Id))
            .ToListAsync(ct);

        return rows.ToDictionary(p => p.Id, MapParty);
    }

    public static async Task<List<PartyView>> GetByAccountIdsAsync(
        IAccountingDbContext db,
        IEnumerable<int> accountIds,
        CancellationToken ct)
    {
        var ids = accountIds.Distinct().ToList();
        if (ids.Count == 0) return new List<PartyView>();

        var rows = await BaseQuery(db)
            .Where(p => ids.Contains(p.AccountId))
            .ToListAsync(ct);

        return rows.Select(MapParty).ToList();
    }

    public static async Task<List<int>> GetAccountIdsByPartyIdsAsync(
        IAccountingDbContext db,
        IEnumerable<int> partyIds,
        CancellationToken ct)
    {
        var ids = partyIds.Distinct().ToList();
        if (ids.Count == 0) return new List<int>();

        return await BaseQuery(db)
            .Where(p => ids.Contains(p.Id))
            .Select(p => p.AccountId)
            .ToListAsync(ct);
    }

    public static async Task<bool> IsCashBoxAccountAsync(
        IAccountingDbContext db,
        int accountId,
        CancellationToken ct)
    {
        return await BaseQuery(db).AnyAsync(p => p.AccountId == accountId, ct);
    }

    public static bool SupportsCurrency(PartyView party, string currency)
    {
        var cur = (currency ?? "IQD").Trim().ToUpperInvariant();
        return party.Currencies.Any(c => c.IsActive
            && string.Equals(c.Currency, cur, StringComparison.OrdinalIgnoreCase));
    }

    public static CurrencyView? GetCurrency(PartyView party, string currency)
    {
        var cur = (currency ?? "IQD").Trim().ToUpperInvariant();
        return party.Currencies.FirstOrDefault(c => c.IsActive
            && string.Equals(c.Currency, cur, StringComparison.OrdinalIgnoreCase));
    }

    public static CashBoxDto ToCashBoxDto(PartyView party, bool hasMovements = false)
    {
        var currencies = party.Currencies
            .OrderBy(c => c.Currency)
            .Select((c, i) => new CashBoxCurrencyDto(
                party.Id * 1000 + i + 1,
                c.Currency,
                c.DebitLimit,
                c.CreditLimit,
                c.IsActive))
            .ToList();

        return new CashBoxDto(
            party.Id,
            party.Code,
            party.NameAr,
            party.NameEn,
            null,
            party.AccountId,
            party.Code,
            party.NameAr,
            party.IsActive,
            party.DisplayOrder,
            currencies,
            hasMovements);
    }

    private static IQueryable<FinancialParty> BaseQuery(IAccountingDbContext db) =>
        db.FinancialParties
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Account)
            .Where(p => p.Category!.Kind == FinancialPartyKind.CashBox && !p.IsDeleted);

    private static PartyView MapParty(FinancialParty p)
    {
        var limits = p.GetCreditLimits();
        var allowed = p.GetAllowedCurrenciesList();
        var currencies = new List<CurrencyView>();

        foreach (var cur in allowed.Select(c => c.Trim().ToUpperInvariant()).Where(c => !string.IsNullOrEmpty(c)).Distinct())
        {
            limits.TryGetValue(cur, out var lim);
            currencies.Add(new CurrencyView(cur, lim?.Debit, lim?.Credit, IsActive: true));
        }

        if (currencies.Count == 0)
        {
            foreach (var kv in limits)
            {
                currencies.Add(new CurrencyView(
                    kv.Key.Trim().ToUpperInvariant(),
                    kv.Value.Debit,
                    kv.Value.Credit,
                    IsActive: true));
            }
        }

        if (currencies.Count == 0)
            currencies.Add(new CurrencyView("IQD", null, null, IsActive: true));

        var displayOrder = (p.Category?.DisplayOrder ?? 100) * 1000 + p.Id;

        return new PartyView(
            p.Id,
            p.Account!.Code,
            p.Account.NameAr,
            p.Account.NameEn,
            p.AccountId,
            p.IsActive,
            displayOrder,
            currencies);
    }
}
