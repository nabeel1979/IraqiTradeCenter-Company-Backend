using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public record CreateFinancialPartyCategoryCommand(
    FinancialPartyKind Kind,
    string NameAr,
    string? NameEn,
    int MainAccountId
) : IRequest<Result<int>>;
