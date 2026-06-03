using IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageAccounts;

/// <summary>
/// حسابات لا يجوز اختيارها في القيود اليومية (صناديق + وسيط تسوية).
/// </summary>
public record GetJournalRestrictedAccountIdsQuery : IRequest<List<int>>;

public class GetJournalRestrictedAccountIdsHandler
    : IRequestHandler<GetJournalRestrictedAccountIdsQuery, List<int>>
{
    private readonly IAccountingDbContext _db;

    public GetJournalRestrictedAccountIdsHandler(IAccountingDbContext db) => _db = db;

    public async Task<List<int>> Handle(GetJournalRestrictedAccountIdsQuery req, CancellationToken ct)
    {
        var cashBoxes = await CashBoxPartySource.GetAllAsync(_db, activeOnly: true, ct);
        var settlementLinked = await AccountSettlementLinkedSource.GetAllLinkedAccountIdsAsync(_db, ct);

        return cashBoxes.Select(b => b.AccountId)
            .Concat(settlementLinked)
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToList();
    }
}
