using IraqiTradeCenterCompany.Modules.Accounting.Application.Features.AccountSettlements;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// حسابات مرتبطة بإعدادات تسوية الحسابات (وسيط + أرباح/خسائر/خصم فرق العملة).
/// </summary>
internal static class AccountSettlementLinkedSource
{
    public const string RoleTransit = "Transit";
    public const string RoleFxGain = "FxGain";
    public const string RoleFxLoss = "FxLoss";
    public const string RoleFxDiscount = "FxDiscount";

    public static async Task<AccountSettlementSettingsDto> GetSettingsAsync(
        IAccountingDbContext db, CancellationToken ct)
    {
        var s = await db.AccountSettlementSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == AccountSettlementSettings.SingletonId, ct);
        return GetAccountSettlementSettingsHandler.MapSettings(s);
    }

    public static Dictionary<int, List<string>> BuildRoleMap(AccountSettlementSettingsDto settings)
    {
        var map = new Dictionary<int, List<string>>();
        void Add(int id, string role)
        {
            if (id <= 0) return;
            if (!map.TryGetValue(id, out var roles))
            {
                roles = new List<string>();
                map[id] = roles;
            }
            if (!roles.Contains(role)) roles.Add(role);
        }

        foreach (var id in settings.TransitAccounts.Values)
            Add(id, RoleTransit);
        if (settings.FxGainAccountId is > 0) Add(settings.FxGainAccountId.Value, RoleFxGain);
        if (settings.FxLossAccountId is > 0) Add(settings.FxLossAccountId.Value, RoleFxLoss);
        if (settings.FxDiscountAccountId is > 0) Add(settings.FxDiscountAccountId.Value, RoleFxDiscount);

        return map;
    }

    public static HashSet<int> CollectAllLinkedIds(AccountSettlementSettingsDto settings)
        => BuildRoleMap(settings).Keys.ToHashSet();

    public static async Task<HashSet<int>> GetAllLinkedAccountIdsAsync(
        IAccountingDbContext db, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(db, ct);
        return CollectAllLinkedIds(settings);
    }

    public static async Task<bool> IsLinkedAccountAsync(
        IAccountingDbContext db, int accountId, CancellationToken ct)
    {
        if (accountId <= 0) return false;
        var ids = await GetAllLinkedAccountIdsAsync(db, ct);
        return ids.Contains(accountId);
    }

    public static async Task<string?> DescribeRolesAsync(
        IAccountingDbContext db, int accountId, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(db, ct);
        var map = BuildRoleMap(settings);
        if (!map.TryGetValue(accountId, out var roles) || roles.Count == 0) return null;
        return string.Join(", ", roles);
    }
}
