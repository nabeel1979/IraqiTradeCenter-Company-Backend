using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialParties;

public record GetFinancialPartiesQuery(
    FinancialPartyKind? Kind = null,
    int? CategoryId = null,
    bool IncludeInactive = false,
    string? Search = null
) : IRequest<List<FinancialPartyDto>>;
