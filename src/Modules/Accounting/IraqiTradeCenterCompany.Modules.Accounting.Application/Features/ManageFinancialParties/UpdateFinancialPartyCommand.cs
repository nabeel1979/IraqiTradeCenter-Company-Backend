using IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialParties;

public record UpdateFinancialPartyCommand(
    int Id,
    string NameAr,
    string? NameEn,
    Dictionary<string, CreditLimitDto>? CreditLimits,
    List<string>? AllowedCurrencies,
    Dictionary<string, string>? CurrencyIbans,
    string? Phone,
    string? Mobile,
    string? Email,
    string? Address,
    string? AddressEn,
    string? ContactPerson,
    string? Notes,
    string? BankAccountNumber,
    string? SwiftCode,
    bool IsActive
) : IRequest<Result<bool>>;
