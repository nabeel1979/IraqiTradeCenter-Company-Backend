using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public class DeleteFinancialPartyCategoryHandler
    : IRequestHandler<DeleteFinancialPartyCategoryCommand, Result<bool>>
{
    private readonly IAccountingDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public DeleteFinancialPartyCategoryHandler(IAccountingDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<bool>> Handle(DeleteFinancialPartyCategoryCommand req, CancellationToken ct)
    {
        var category = await _db.FinancialPartyCategories
            .Include(c => c.MainAccount)
            .FirstOrDefaultAsync(c => c.Id == req.Id, ct);
        if (category is null) return Result.Failure<bool>("النوع غير موجود");

        var hasParties = await _db.FinancialParties
            .AnyAsync(p => p.CategoryId == req.Id, ct);
        if (hasParties)
            return Result.Failure<bool>(
                "لا يمكن حذف النوع لأنه يحتوي على أطراف مالية — احذف الأطراف أولاً");

        // فك قفل الحساب الرئيسي
        category.MainAccount.UnlockForParties();

        var userId = _currentUser.UserId?.ToString();
        category.MarkAsDeleted(userId);
        await _db.SaveChangesAsync(ct);
        return Result.Success(true);
    }
}
