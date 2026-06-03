using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialParties;

public class DeleteFinancialPartyHandler
    : IRequestHandler<DeleteFinancialPartyCommand, Result<bool>>
{
    private readonly IAccountingDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public DeleteFinancialPartyHandler(IAccountingDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<bool>> Handle(DeleteFinancialPartyCommand req, CancellationToken ct)
    {
        var party = await _db.FinancialParties
            .Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.Id == req.Id, ct);
        if (party is null) return Result.Failure<bool>("الطرف غير موجود");

        // التحقق من عدم استخدام الحساب في قيود
        var accountId = party.AccountId;
        var inUse = await _db.JournalEntryLines.AnyAsync(l => l.AccountId == accountId, ct);
        if (inUse)
            return Result.Failure<bool>(
                "لا يمكن حذف الطرف لأن حسابه مرتبط بقيود محاسبية — أرشفه بدلاً من الحذف");

        var userId = _currentUser.UserId?.ToString();
        party.Account.MarkAsDeleted(userId);
        party.MarkAsDeleted(userId);
        await _db.SaveChangesAsync(ct);
        return Result.Success(true);
    }
}
