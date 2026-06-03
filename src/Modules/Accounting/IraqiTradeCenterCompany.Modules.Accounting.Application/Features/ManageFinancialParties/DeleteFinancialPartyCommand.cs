using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialParties;

public record DeleteFinancialPartyCommand(int Id) : IRequest<Result<bool>>;
