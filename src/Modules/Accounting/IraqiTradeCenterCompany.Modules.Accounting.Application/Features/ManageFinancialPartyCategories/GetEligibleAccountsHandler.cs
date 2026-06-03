using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public class GetEligibleAccountsHandler
    : IRequestHandler<GetEligibleAccountsQuery, List<EligibleAccountDto>>
{
    private readonly IAccountingDbContext _db;
    public GetEligibleAccountsHandler(IAccountingDbContext db) => _db = db;

    public async Task<List<EligibleAccountDto>> Handle(
        GetEligibleAccountsQuery req, CancellationToken ct)
    {
        // ‎مجموعة الحسابات المرتبطة بقيود محاسبية — نستثنيها لاحقاً.
        var usedInJournal = _db.JournalEntryLines
            .AsNoTracking()
            .Select(l => l.AccountId)
            .Distinct();

        var rows = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.IsActive
                     && a.IsLeaf
                     && !a.IsLockedForParties
                     && !usedInJournal.Contains(a.Id))
            .OrderBy(a => a.Code)
            .Select(a => new EligibleAccountDto
            {
                Id     = a.Id,
                Code   = a.Code,
                NameAr = a.NameAr,
                NameEn = a.NameEn,
            })
            .ToListAsync(ct);

        return rows;
    }
}
