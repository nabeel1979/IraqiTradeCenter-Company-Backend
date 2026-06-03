using System.Data;
using System.Data.Common;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.FiscalYearManagement;

public record GetFiscalYearStatusQuery(int FiscalYearId) : IRequest<FiscalYearStatusDto?>;

public class GetFiscalYearStatusHandler : IRequestHandler<GetFiscalYearStatusQuery, FiscalYearStatusDto?>
{
    private readonly IAccountingDbContext _db;
    public GetFiscalYearStatusHandler(IAccountingDbContext db) => _db = db;

    public async Task<FiscalYearStatusDto?> Handle(GetFiscalYearStatusQuery req, CancellationToken ct)
    {
        var fy = await _db.FiscalYears.AsNoTracking().FirstOrDefaultAsync(f => f.Id == req.FiscalYearId, ct);
        if (fy == null) return null;

        var periods = await _db.AccountingPeriods.AsNoTracking()
            .Where(p => p.FiscalYearId == req.FiscalYearId).ToListAsync(ct);

        var entries = await _db.JournalEntries.AsNoTracking()
            .Where(e => e.EntryDate >= fy.StartDate && e.EntryDate <= fy.EndDate)
            .Select(e => new { e.Id, e.Status })
            .ToListAsync(ct);

        var draft = entries.Count(e => e.Status == JournalEntryStatus.Draft);
        var posted = entries.Count(e => e.Status == JournalEntryStatus.Posted);

        var sums = await (
            from line in _db.JournalEntryLines.AsNoTracking()
            join entry in _db.JournalEntries on line.JournalEntryId equals entry.Id
            where entry.Status == JournalEntryStatus.Posted
                && entry.EntryDate >= fy.StartDate && entry.EntryDate <= fy.EndDate
            select line
        ).ToListAsync(ct);

        var totalD = sums.Where(l => l.IsDebit).Sum(l => l.Amount);
        var totalC = sums.Where(l => !l.IsDebit).Sum(l => l.Amount);

        // ‎حالة التدوير
        var nextFy = await _db.FiscalYears.AsNoTracking()
            .Where(f => f.StartDate > fy.EndDate)
            .OrderBy(f => f.StartDate)
            .FirstOrDefaultAsync(ct);

        var priorFy = await _db.FiscalYears.AsNoTracking()
            .Where(f => f.EndDate < fy.StartDate)
            .OrderByDescending(f => f.EndDate)
            .FirstOrDefaultAsync(ct);

        var laterFys = await _db.FiscalYears.AsNoTracking()
            .Where(f => f.StartDate > fy.EndDate)
            .OrderBy(f => f.StartDate)
            .ToListAsync(ct);

        async Task<int> CountOpeningAsync(int fiscalYearId) =>
            await _db.JournalEntries.AsNoTracking()
                .CountAsync(e => e.FiscalYearId == fiscalYearId
                              && e.EntryType == JournalEntryType.Opening
                              && !e.IsDeleted, ct);

        var undoTargets = new List<RolloverUndoTargetDto>();

        // ‎تدوير وارد: قيود افتتاحية في السنة الحالية (عرض من السنة الهدف المفتوحة)
        var incomingCount = await CountOpeningAsync(fy.Id);
        if (incomingCount > 0)
        {
            undoTargets.Add(new RolloverUndoTargetDto
            {
                TargetFiscalYearId = fy.Id,
                TargetFiscalYearName = fy.Name,
                TargetFiscalYearNameEn = fy.NameEn,
                OpeningEntriesCount = incomingCount,
                SourceFiscalYearId = priorFy?.Id,
                SourceFiscalYearName = priorFy?.Name,
                SourceFiscalYearNameEn = priorFy?.NameEn,
            });
        }

        // ‎تدوير صادر: قيود افتتاحية في سنوات لاحقة (عرض من السنة المصدر المغلقة)
        foreach (var target in laterFys)
        {
            if (target.Id == fy.Id) continue;
            var count = await CountOpeningAsync(target.Id);
            if (count <= 0) continue;
            undoTargets.Add(new RolloverUndoTargetDto
            {
                TargetFiscalYearId = target.Id,
                TargetFiscalYearName = target.Name,
                TargetFiscalYearNameEn = target.NameEn,
                OpeningEntriesCount = count,
                SourceFiscalYearId = fy.Id,
                SourceFiscalYearName = fy.Name,
                SourceFiscalYearNameEn = fy.NameEn,
            });
        }

        undoTargets = undoTargets
            .GroupBy(t => t.TargetFiscalYearId)
            .Select(g => g.First())
            .OrderBy(t => t.TargetFiscalYearId)
            .ToList();

        var rolloverOpeningCount = nextFy != null ? await CountOpeningAsync(nextFy.Id) : 0;

        return new FiscalYearStatusDto
        {
            FiscalYearId = fy.Id,
            FiscalYearName = fy.Name,
            FiscalYearNameEn = fy.NameEn,
            StartDate = fy.StartDate,
            EndDate = fy.EndDate,
            IsClosed = fy.IsClosed,
            ClosedAt = fy.ClosedAt,
            TotalPeriods = periods.Count,
            OpenPeriods = periods.Count(p => p.Status == PeriodStatus.Open),
            ClosedPeriods = periods.Count(p => p.Status == PeriodStatus.Closed),
            LockedPeriods = periods.Count(p => p.Status == PeriodStatus.Locked),
            DraftEntries = draft,
            PostedEntries = posted,
            TotalDebits = totalD,
            TotalCredits = totalC,
            IsBalanced = Math.Abs(totalD - totalC) < 0.01m,
            RolloverTargetFiscalYearId = nextFy?.Id,
            RolloverTargetFiscalYearName = nextFy?.Name,
            RolloverTargetFiscalYearNameEn = nextFy?.NameEn,
            HasRolloverOpeningEntries = undoTargets.Count > 0,
            RolloverOpeningEntriesCount = undoTargets.Sum(t => t.OpeningEntriesCount),
            RolloverUndoTargets = undoTargets,
        };
    }
}
