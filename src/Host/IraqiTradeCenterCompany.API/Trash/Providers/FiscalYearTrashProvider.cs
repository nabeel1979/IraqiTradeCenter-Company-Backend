using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Models;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.API.Trash.Providers;

public class FiscalYearTrashProvider : ITrashProvider
{
    private readonly IAccountingDbContext _db;
    public FiscalYearTrashProvider(IAccountingDbContext db) { _db = db; }

    public string EntityType => "FiscalYear";

    public async Task<List<TrashItemDto>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.FiscalYears.IgnoreQueryFilters().AsNoTracking()
            .Where(f => f.IsDeleted)
            .OrderByDescending(f => f.DeletedAt)
            .Select(f => new { f.Id, f.Name, f.NameEn, f.StartDate, f.EndDate, f.DeletedAt, f.UpdatedBy })
            .ToListAsync(ct);

        return rows.Select(r => new TrashItemDto
        {
            EntityType = EntityType,
            EntityTypeLabel = "سنة مالية",
            Module = "المحاسبة",
            Icon = "CalendarRange",
            EntityId = r.Id,
            DisplayName = string.IsNullOrWhiteSpace(r.NameEn) ? r.Name : $"{r.Name} / {r.NameEn}",
            SubInfo = $"{r.StartDate:yyyy/MM/dd} → {r.EndDate:yyyy/MM/dd}",
            DeletedAt = r.DeletedAt,
            DeletedBy = r.UpdatedBy,
        }).ToList();
    }

    public async Task<Result> RestoreAsync(int id, CancellationToken ct)
    {
        var fy = await _db.FiscalYears.IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fy is null) return Result.Failure("السنة المالية غير موجودة");
        if (!fy.IsDeleted) return Result.Failure("السنة ليست في السلة");
        fy.Restore();
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> PermanentlyDeleteAsync(int id, CancellationToken ct)
    {
        var fy = await _db.FiscalYears.IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fy is null) return Result.Failure("السنة المالية غير موجودة");
        if (!fy.IsDeleted) return Result.Failure("الحذف النهائي مسموح فقط من السلة");

        // 1) سطور القيود المحاسبية
        var entryIds = await _db.JournalEntries.IgnoreQueryFilters()
            .Where(e => e.FiscalYearId == id)
            .Select(e => e.Id)
            .ToListAsync(ct);

        if (entryIds.Count > 0)
        {
            // مرفقات السندات
            var attachments = await _db.VoucherAttachments.IgnoreQueryFilters()
                .Where(a => entryIds.Contains(a.JournalEntryId))
                .ToListAsync(ct);
            _db.VoucherAttachments.RemoveRange(attachments);

            // سطور القيود
            var lines = await _db.JournalEntryLines.IgnoreQueryFilters()
                .Where(l => entryIds.Contains(l.JournalEntryId))
                .ToListAsync(ct);
            _db.JournalEntryLines.RemoveRange(lines);

            // القيود نفسها
            var entries = await _db.JournalEntries.IgnoreQueryFilters()
                .Where(e => entryIds.Contains(e.Id))
                .ToListAsync(ct);
            _db.JournalEntries.RemoveRange(entries);
        }

        // 2) الفترات المحاسبية
        var periods = await _db.AccountingPeriods.IgnoreQueryFilters()
            .Where(p => p.FiscalYearId == id).ToListAsync(ct);
        _db.AccountingPeriods.RemoveRange(periods);

        // 3) السنة المالية
        _db.FiscalYears.Remove(fy);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
