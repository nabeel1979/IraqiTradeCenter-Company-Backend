using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public record GetFinancialPartyCategoriesQuery(
    FinancialPartyKind? Kind = null,
    bool IncludeInactive = false
) : IRequest<List<FinancialPartyCategoryDto>>;
