using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using System.Text.Json;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.SharedKernel.Common;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// DbContext خاص بمودول المحاسبة - يستخدم schema منفصل "acc"
/// </summary>
public class AccountingDbContext : DbContext, IAccountingDbContext
{
    public const string Schema = "acc";
    private readonly ICurrentUserService? _currentUser;

    public AccountingDbContext(DbContextOptions<AccountingDbContext> options,
                                ICurrentUserService? currentUser = null) : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalEntryLine> JournalEntryLines => Set<JournalEntryLine>();
    public DbSet<CurrencyRateBulletin> CurrencyRateBulletins => Set<CurrencyRateBulletin>();
    public DbSet<CurrencyRateLine> CurrencyRateLines => Set<CurrencyRateLine>();
    public DbSet<JournalVoucherType> JournalVoucherTypes => Set<JournalVoucherType>();
    public DbSet<CashBox> CashBoxes => Set<CashBox>();
    public DbSet<CashBoxCurrency> CashBoxCurrencies => Set<CashBoxCurrency>();
    public DbSet<CashBoxTransfer> CashBoxTransfers => Set<CashBoxTransfer>();
    public DbSet<VoucherAttachment> VoucherAttachments => Set<VoucherAttachment>();
    public DbSet<AttachmentSyncOutbox> AttachmentSyncOutbox => Set<AttachmentSyncOutbox>();
    public DbSet<FinancialPartyCategory> FinancialPartyCategories => Set<FinancialPartyCategory>();
    public DbSet<FinancialParty> FinancialParties => Set<FinancialParty>();
    public DbSet<AccountSettlement> AccountSettlements => Set<AccountSettlement>();
    public DbSet<AccountSettlementSettings> AccountSettlementSettings => Set<AccountSettlementSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccountingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var userId = _currentUser?.UserId?.ToString() ?? "system";
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.SetCreated(userId);
            else if (entry.State == EntityState.Modified) entry.Entity.SetUpdated(userId);
        }
        return base.SaveChangesAsync(ct);
    }

    public async Task<long> GetNextJournalEntryNumberAsync(int fiscalYearId, CancellationToken ct = default)
    {
        // ترقيم متسلسل لكل سنة مالية:
        //  - نقفل ذرّياً على مستوى السنة المالية بـ sp_getapplock لمنع تكرار الرقم
        //    عند الطلبات المتزامنة (UPDLOCK وحده لا يحجز شيئاً عند عدم وجود صفوف).
        //  - نأخذ MAX من كل القيود (سواء محذوفة أم لا) كي لا نُعيد استخدام أرقام
        //    قيود محذوفة سابقاً — مهم لمسار التدقيق المحاسبي.
        //  - يجب أن تكون هناك معاملة قائمة من الـ Caller لأن LockOwner='Transaction'
        //    يضمن استمرار القفل حتى تُلتزم المعاملة (بعد إدراج القيد).
        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "GetNextJournalEntryNumberAsync يجب أن تُستدعى داخل معاملة (BeginTransactionAsync).");
        }

        // (1) نضع قفلاً ذرّياً على الـ resource المرتبط بالسنة المالية.
        //     LockOwner='Transaction' يضمن أن القفل يبقى رفيعاً حتى Commit/Rollback.
        var lockSql = @"
DECLARE @res INT;
EXEC @res = sp_getapplock
    @Resource    = @resource,
    @LockMode    = 'Exclusive',
    @LockOwner   = 'Transaction',
    @LockTimeout = 8000;
IF @res < 0
    THROW 51000, N'تعذّر الحصول على قفل توليد رقم القيد، حاول مرة أخرى', 1;";
        var resourceParam = new Microsoft.Data.SqlClient.SqlParameter("@resource",
            $"acc.JournalEntryNumber.FY:{fiscalYearId}");
        await Database.ExecuteSqlRawAsync(lockSql, new[] { resourceParam }, ct);

        // (2) نقرأ MAX من كل القيود (نشطة + محذوفة) ضمن نفس السنة، مع +1.
        //     نأخذ المحذوفة في الحسبان لمنع إعادة استخدام أرقام لقيود سابقة (audit trail).
        const string maxSql = @"
SELECT ISNULL(MAX(TRY_CAST(EntryNumber AS BIGINT)), 0) + 1 AS Value
FROM acc.JournalEntries
WHERE FiscalYearId = {0}
  AND TRY_CAST(EntryNumber AS BIGINT) IS NOT NULL";

        return await Database
            .SqlQueryRaw<long>(maxSql, fiscalYearId)
            .FirstAsync(ct);
    }

    public async Task<int> GetNextVoucherSequenceAsync(int voucherTypeId, int fiscalYearId, CancellationToken ct = default)
    {
        // ترقيم مستقل لكل نوع سند داخل كل سنة مالية: PV-1, PV-2 … (يبدأ من 1
        // لكل نوع سند في كل سنة مالية على حدة — إعادة الترقيم سنوياً).
        // نفس نمط GetNextJournalEntryNumberAsync لكن المورد مفصول حسب
        // (VoucherTypeId + FiscalYearId) لمنع تكرار الرقم عند الطلبات المتزامنة.
        if (Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "GetNextVoucherSequenceAsync يجب أن تُستدعى داخل معاملة (BeginTransactionAsync).");
        }

        var lockSql = @"
DECLARE @res INT;
EXEC @res = sp_getapplock
    @Resource    = @resource,
    @LockMode    = 'Exclusive',
    @LockOwner   = 'Transaction',
    @LockTimeout = 8000;
IF @res < 0
    THROW 51000, N'تعذّر الحصول على قفل توليد رقم السند، حاول مرة أخرى', 1;";
        var resourceParam = new Microsoft.Data.SqlClient.SqlParameter("@resource",
            $"acc.VoucherSequence.VT:{voucherTypeId}.FY:{fiscalYearId}");
        await Database.ExecuteSqlRawAsync(lockSql, new[] { resourceParam }, ct);

        // نقرأ MAX من كل القيود (نشطة + محذوفة) لنفس نوع السند ونفس السنة المالية، مع +1.
        // إدراج المحذوفة يحفظ تسلسل التدقيق ويمنع إعادة استخدام نفس رقم سند.
        const string maxSql = @"
SELECT ISNULL(MAX(VoucherSequence), 0) + 1 AS Value
FROM acc.JournalEntries
WHERE VoucherTypeId = {0}
  AND FiscalYearId = {1}
  AND VoucherSequence IS NOT NULL";

        return await Database
            .SqlQueryRaw<int>(maxSql, voucherTypeId, fiscalYearId)
            .FirstAsync(ct);
    }

    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(
        CancellationToken ct = default)
        => Database.BeginTransactionAsync(ct);

    public System.Data.Common.DbConnection GetDbConnection() => Database.GetDbConnection();

    public void ConfigureDbCommand(System.Data.Common.DbCommand command)
    {
        var tx = Database.CurrentTransaction?.GetDbTransaction();
        if (tx != null)
            command.Transaction = tx;
    }

    public async Task EnsureAccountSettlementSettingsRowAsync(CancellationToken ct = default)
    {
        var exists = await AccountSettlementSettings
            .AsNoTracking()
            .AnyAsync(x => x.Id == Domain.Entities.AccountSettlementSettings.SingletonId, ct);
        if (exists) return;

        await Database.ExecuteSqlRawAsync(@"
SET IDENTITY_INSERT acc.AccountSettlementSettings ON;
INSERT INTO acc.AccountSettlementSettings (Id, CreatedAt, IsDeleted)
VALUES (1, GETUTCDATE(), 0);
SET IDENTITY_INSERT acc.AccountSettlementSettings OFF;
", ct);
    }

    public async Task SyncAccountSettlementTransitExclusionsAsync(CancellationToken ct = default)
    {
        var settings = await AccountSettlementSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == Domain.Entities.AccountSettlementSettings.SingletonId, ct);
        if (settings?.TransitAccountsJson is not { Length: > 0 } json)
            return;

        List<int> transitIds;
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            transitIds = parsed?.Values.Where(id => id > 0).Distinct().ToList() ?? [];
        }
        catch
        {
            return;
        }

        if (transitIds.Count == 0) return;

        var accounts = await Accounts
            .Where(a => transitIds.Contains(a.Id) && !a.IsExcludedFromReports)
            .ToListAsync(ct);
        if (accounts.Count == 0) return;

        foreach (var acc in accounts)
            acc.SetExcludedFromReports(true);
        await SaveChangesAsync(ct);
    }
}
