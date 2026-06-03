using AutoMapper;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.GetAccountsTree;

public class GetAccountsTreeHandler : IRequestHandler<GetAccountsTreeQuery, List<AccountDto>>
{
    private readonly IAccountingDbContext _db;
    private readonly IMapper _mapper;
    public GetAccountsTreeHandler(IAccountingDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }

    public async Task<List<AccountDto>> Handle(GetAccountsTreeQuery req, CancellationToken ct)
    {
        // ‎عند includeInactive=true نُرجع كل الحسابات (مفعَّلة + معطَّلة) كي تظهر في
        // ‎شاشة إدارة الشجرة، مع وَسم كل عنصر بحقل IsActive في الـ DTO. أما الاستدعاء
        // ‎الافتراضي (شاشات اختيار الحسابات: قيود/صناديق/سندات) فيستثني المعطَّلة.
        var query = _db.Accounts.AsNoTracking();
        if (!req.IncludeInactive)
            query = query.Where(a => a.IsActive);
        var all = await query.OrderBy(a => a.Code).ToListAsync(ct);
        var dtos = _mapper.Map<List<AccountDto>>(all);

        // ‎نجمع الحسابات المرتبطة من كل المصادر — قيود + صناديق + أنواع سندات +
        // ‎أرصدة افتتاحية — في مجموعة واحدة (Set من Ids) ثم نُعلّم الـ DTOs.
        var usedInLines = await _db.JournalEntryLines.AsNoTracking()
            .Select(l => l.AccountId).Distinct().ToListAsync(ct);
        var usedInCashBoxes = await _db.FinancialParties.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.Category!.Kind == FinancialPartyKind.CashBox)
            .Select(p => p.AccountId)
            .Distinct()
            .ToListAsync(ct);
        var usedInVoucherDebit = await _db.JournalVoucherTypes.AsNoTracking()
            .Where(v => v.DefaultDebitAccountId.HasValue)
            .Select(v => v.DefaultDebitAccountId!.Value).Distinct().ToListAsync(ct);
        var usedInVoucherCredit = await _db.JournalVoucherTypes.AsNoTracking()
            .Where(v => v.DefaultCreditAccountId.HasValue)
            .Select(v => v.DefaultCreditAccountId!.Value).Distinct().ToListAsync(ct);

        var usedIds = new HashSet<int>(usedInLines);
        usedIds.UnionWith(usedInCashBoxes);
        usedIds.UnionWith(usedInVoucherDebit);
        usedIds.UnionWith(usedInVoucherCredit);
        // ‎الحساب ذو الرصيد الافتتاحي يُعتبر مستخدماً (لا يُحذف ولا يُحوّل لأبٍ)
        foreach (var a in all.Where(a => a.OpeningBalance != 0))
            usedIds.Add(a.Id);

        var fmPartyAccountIds = await _db.FinancialParties.AsNoTracking()
            .Select(p => p.AccountId)
            .ToListAsync(ct);
        var fmPartySet = fmPartyAccountIds.ToHashSet();

        var settlementSettings = await AccountSettlementLinkedSource.GetSettingsAsync(_db, ct);
        var settlementRoleMap = AccountSettlementLinkedSource.BuildRoleMap(settlementSettings);

        foreach (var d in dtos)
        {
            d.IsUsed = usedIds.Contains(d.Id);
            d.IsManagedByFinancialManagement = d.IsLockedForParties || fmPartySet.Contains(d.Id);
            if (settlementRoleMap.TryGetValue(d.Id, out var roles))
            {
                d.IsLinkedToAccountSettlement = true;
                d.AccountSettlementRoles = roles;
                d.IsUsed = true;
            }
        }

        var lookup = dtos.ToDictionary(a => a.Id);
        var roots = new List<AccountDto>();
        foreach (var d in dtos)
        {
            if (d.ParentId.HasValue && lookup.TryGetValue(d.ParentId.Value, out var parent))
                parent.Children.Add(d);
            else roots.Add(d);
        }
        return roots;
    }
}
