using IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence;

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Seed;

/// <summary>
/// يضمن وجود صف إعدادات التسوية (Id=1) عند بدء التشغيل.
/// </summary>
public static class AccountSettlementSettingsSeeder
{
    public static async Task SeedAsync(AccountingDbContext db, CancellationToken ct = default)
    {
        await db.EnsureAccountSettlementSettingsRowAsync(ct);
        await db.SyncAccountSettlementTransitExclusionsAsync(ct);
    }
}
