using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public record DeleteFinancialPartyCategoryCommand(int Id) : IRequest<Result<bool>>;
