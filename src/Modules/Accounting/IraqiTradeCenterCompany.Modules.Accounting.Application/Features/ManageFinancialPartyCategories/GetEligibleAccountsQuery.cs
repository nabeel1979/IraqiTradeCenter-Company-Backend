using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

/// <summary>
/// يُرجع الحسابات الصالحة لربطها بنوع طرف:
/// ورقة (IsLeaf) + غير مقفلة (IsLockedForParties=false) + لا قيود محاسبية عليها.
/// </summary>
public class GetEligibleAccountsQuery : IRequest<List<EligibleAccountDto>>
{
}
