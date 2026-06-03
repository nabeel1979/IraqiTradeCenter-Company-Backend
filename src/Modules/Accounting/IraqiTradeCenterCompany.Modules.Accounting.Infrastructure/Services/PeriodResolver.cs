using IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Exceptions;
using IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Infrastructure.Services;

internal class PeriodResolver : IPeriodResolver
{
    private readonly AccountingDbContext _db;
    public PeriodResolver(AccountingDbContext db) => _db = db;

    public async Task<(int FiscalYearId, int PeriodId)> ResolveAsync(DateTime date, CancellationToken ct = default)
    {
        // ‎عند تداخل فترات سنوات متعددة (مثلاً سنة جزئية + سنة كاملة بنفس البداية):
        // ‎نفضّل السنة المُفعَّلة IsActive، ثم الأحدث بدايةً — حتى تُسجَّل القيود في السنة الصحيحة.
        var d = date.Date;
        var matches = await (
            from p in _db.AccountingPeriods.AsNoTracking()
            join fy in _db.FiscalYears.AsNoTracking() on p.FiscalYearId equals fy.Id
            where p.StartDate <= d && p.EndDate >= d
            select new { Period = p, FiscalYear = fy }
        ).ToListAsync(ct);

        var chosen = matches
            .OrderByDescending(x => x.FiscalYear.IsActive)
            .ThenByDescending(x => x.FiscalYear.StartDate)
            .ThenByDescending(x => x.Period.StartDate)
            .FirstOrDefault();

        var period = chosen?.Period;
        if (period == null) throw new DomainException($"لا توجد فترة محاسبية للتاريخ {date:yyyy-MM-dd}");
        if (period.Status == PeriodStatus.Closed || period.Status == PeriodStatus.Locked)
            throw new ClosedPeriodException(date);
        return (period.FiscalYearId, period.Id);
    }
}
