using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public record UpdateFinancialPartyCategoryCommand(
    int Id,
    string NameAr,
    string? NameEn,
    bool IsActive
) : IRequest<Result<bool>>;
