using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public class UpdateFinancialPartyCategoryHandler
    : IRequestHandler<UpdateFinancialPartyCategoryCommand, Result<bool>>
{
    private readonly IAccountingDbContext _db;
    public UpdateFinancialPartyCategoryHandler(IAccountingDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(UpdateFinancialPartyCategoryCommand req, CancellationToken ct)
    {
        try
        {
            var category = await _db.FinancialPartyCategories
                .FirstOrDefaultAsync(c => c.Id == req.Id, ct);
            if (category is null) return Result.Failure<bool>("النوع غير موجود");

            var dupName = await _db.FinancialPartyCategories
                .AnyAsync(c => c.Kind == category.Kind && c.NameAr == req.NameAr.Trim() && c.Id != req.Id, ct);
            if (dupName)
                return Result.Failure<bool>("يوجد نوع بنفس الاسم — استخدم اسماً مختلفاً");

            category.Update(req.NameAr, req.NameEn);
            if (req.IsActive) category.Activate(); else category.Deactivate();

            await _db.SaveChangesAsync(ct);
            return Result.Success(true);
        }
        catch (DomainException ex) { return Result.Failure<bool>(ex.Message); }
    }
}
