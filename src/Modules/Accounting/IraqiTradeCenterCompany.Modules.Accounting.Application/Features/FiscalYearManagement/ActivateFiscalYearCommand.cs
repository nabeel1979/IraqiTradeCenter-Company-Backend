using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.FiscalYearManagement;

/// <summary>
/// تفعيل سنة مالية كـ"نشطة" للتقارير والقراءة — تُلغى تلقائياً تفعيل بقية السنوات.
/// مسموح حتى للسنة المغلقة (الإقفال يمنع القيود لا العرض).
/// </summary>
public record ActivateFiscalYearCommand(int FiscalYearId) : IRequest<Unit>;

public class ActivateFiscalYearHandler : IRequestHandler<ActivateFiscalYearCommand, Unit>
{
    private readonly IAccountingDbContext _db;
    public ActivateFiscalYearHandler(IAccountingDbContext db) => _db = db;

    public async Task<Unit> Handle(ActivateFiscalYearCommand req, CancellationToken ct)
    {
        var target = await _db.FiscalYears.FirstOrDefaultAsync(f => f.Id == req.FiscalYearId, ct)
            ?? throw new DomainException("السنة المالية غير موجودة");

        // ‎إلغاء تفعيل أي سنة أخرى نشطة حالياً
        var others = await _db.FiscalYears
            .Where(f => f.Id != req.FiscalYearId && f.IsActive)
            .ToListAsync(ct);
        foreach (var fy in others) fy.Deactivate();

        target.Activate();
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

/// <summary>
/// استعلام عن السنة المالية المفعَّلة (إن وُجدت). يُستعمل من الـ frontend
/// كمصدر وحيد للحقيقة في كل التقارير والشاشات الافتراضية.
/// </summary>
public record GetActiveFiscalYearQuery() : IRequest<Dtos.FiscalYearDto?>;

public class GetActiveFiscalYearHandler : IRequestHandler<GetActiveFiscalYearQuery, Dtos.FiscalYearDto?>
{
    private readonly IAccountingDbContext _db;
    public GetActiveFiscalYearHandler(IAccountingDbContext db) => _db = db;

    public async Task<Dtos.FiscalYearDto?> Handle(GetActiveFiscalYearQuery req, CancellationToken ct)
    {
        // ‎عند وجود أكثر من سنة نشطة (بيانات قديمة): الأحدث بدايةً.
        var fy = await _db.FiscalYears.AsNoTracking()
            .Where(f => f.IsActive)
            .OrderByDescending(f => f.StartDate)
            .FirstOrDefaultAsync(ct);
        if (fy is null) return null;

        return new Dtos.FiscalYearDto
        {
            Id = fy.Id,
            Name = fy.Name,
            NameEn = fy.NameEn,
            StartDate = fy.StartDate,
            EndDate = fy.EndDate,
            IsClosed = fy.IsClosed,
            ClosedAt = fy.ClosedAt,
            IsActive = fy.IsActive,
        };
    }
}
