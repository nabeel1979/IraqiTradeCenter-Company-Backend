using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public class GetFinancialPartyCategoriesHandler
    : IRequestHandler<GetFinancialPartyCategoriesQuery, List<FinancialPartyCategoryDto>>
{
    private readonly IAccountingDbContext _db;
    public GetFinancialPartyCategoriesHandler(IAccountingDbContext db) => _db = db;

    public async Task<List<FinancialPartyCategoryDto>> Handle(
        GetFinancialPartyCategoriesQuery req, CancellationToken ct)
    {
        var q = _db.FinancialPartyCategories
            .AsNoTracking()
            .Include(c => c.MainAccount)
            .AsQueryable();

        if (req.Kind.HasValue)
            q = q.Where(c => c.Kind == req.Kind.Value);
        if (!req.IncludeInactive)
            q = q.Where(c => c.IsActive);

        var list = await q.OrderBy(c => c.Kind).ThenBy(c => c.DisplayOrder).ToListAsync(ct);

        var ids = list.Select(c => c.Id).ToList();
        var counts = await _db.FinancialParties
            .Where(p => ids.Contains(p.CategoryId))
            .GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        return list.Select(c => new FinancialPartyCategoryDto
        {
            Id                 = c.Id,
            Kind               = c.Kind,
            NameAr             = c.NameAr,
            NameEn             = c.NameEn,
            MainAccountId      = c.MainAccountId,
            MainAccountCode    = c.MainAccount.Code,
            MainAccountNameAr  = c.MainAccount.NameAr,
            MainAccountNameEn  = c.MainAccount.NameEn,
            IsActive           = c.IsActive,
            DisplayOrder       = c.DisplayOrder,
            PartyCount         = counts.TryGetValue(c.Id, out var cnt) ? cnt : 0,
        }).ToList();
    }
}
