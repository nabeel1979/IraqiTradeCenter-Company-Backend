using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialParties;

public class GetFinancialPartiesHandler
    : IRequestHandler<GetFinancialPartiesQuery, List<FinancialPartyDto>>
{
    private readonly IAccountingDbContext _db;
    public GetFinancialPartiesHandler(IAccountingDbContext db) => _db = db;

    public async Task<List<FinancialPartyDto>> Handle(GetFinancialPartiesQuery req, CancellationToken ct)
    {
        var q = _db.FinancialParties
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Account)
            .AsQueryable();

        if (req.Kind.HasValue)
            q = q.Where(p => p.Category.Kind == req.Kind.Value);
        if (req.CategoryId.HasValue)
            q = q.Where(p => p.CategoryId == req.CategoryId.Value);
        if (!req.IncludeInactive)
            q = q.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var s = req.Search.Trim().ToLower();
            q = q.Where(p => p.Account.NameAr.ToLower().Contains(s)
                           || (p.Account.NameEn != null && p.Account.NameEn.ToLower().Contains(s))
                           || p.Account.Code.Contains(s));
        }

        var list = await q.OrderBy(p => p.Category.Kind)
                          .ThenBy(p => p.CategoryId)
                          .ThenBy(p => p.Account.NameAr)
                          .ToListAsync(ct);

        return list.Select(p => new FinancialPartyDto
        {
            Id               = p.Id,
            CategoryId       = p.CategoryId,
            CategoryNameAr   = p.Category.NameAr,
            CategoryNameEn   = p.Category.NameEn,
            Kind             = p.Category.Kind,
            // ‎الاسم يأتي حصراً من الحساب — مزامنة كاملة مع شجرة الحسابات.
            NameAr           = p.Account.NameAr,
            NameEn           = p.Account.NameEn,
            AccountId        = p.AccountId,
            AccountCode      = p.Account.Code,
            CreditLimits     = p.GetCreditLimits().ToDictionary(
                                  kv => kv.Key,
                                  kv => new CreditLimitDto { Debit = kv.Value.Debit, Credit = kv.Value.Credit }),
            AllowedCurrencies = p.GetAllowedCurrenciesList(),
            CurrencyIbans     = p.GetCurrencyIbans(),
            Phone            = p.Phone,
            Mobile           = p.Mobile,
            Email            = p.Email,
            Address          = p.Address,
            AddressEn        = p.AddressEn,
            ContactPerson    = p.ContactPerson,
            Notes            = p.Notes,
            BankAccountNumber = p.BankAccountNumber,
            SwiftCode        = p.SwiftCode,
            IsActive         = p.IsActive,
            CreatedAt        = p.CreatedAt,
        }).ToList();
    }
}
