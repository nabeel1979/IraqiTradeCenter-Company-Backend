using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialParties;

public class UpdateFinancialPartyHandler
    : IRequestHandler<UpdateFinancialPartyCommand, Result<bool>>
{
    private readonly IAccountingDbContext _db;
    public UpdateFinancialPartyHandler(IAccountingDbContext db) => _db = db;

    public async Task<Result<bool>> Handle(UpdateFinancialPartyCommand req, CancellationToken ct)
    {
        try
        {
            var party = await _db.FinancialParties
                .Include(p => p.Account)
                .Include(p => p.Category)
                .FirstOrDefaultAsync(p => p.Id == req.Id, ct);
            if (party is null) return Result.Failure<bool>("الطرف غير موجود");

            if (string.IsNullOrWhiteSpace(req.NameAr))
                return Result.Failure<bool>("اسم الطرف مطلوب");
            var nameAr = req.NameAr.Trim();
            var nameEn = req.NameEn?.Trim();
            var parentId = party.Account.ParentId;

            // ‎منع التكرار (مع استثناء الطرف نفسه): الاسم العربي/الإنجليزي ضمن نفس النوع،
            // ‎والهاتف/البريد عبر كل الأطراف.
            var dupNameAr = await _db.Accounts
                .AnyAsync(a => a.ParentId == parentId && a.NameAr == nameAr && a.Id != party.AccountId, ct);
            if (dupNameAr)
                return Result.Failure<bool>("الاسم العربي مكرر في هذا النوع");

            if (!string.IsNullOrWhiteSpace(nameEn))
            {
                var dupNameEn = await _db.Accounts
                    .AnyAsync(a => a.ParentId == parentId && a.NameEn == nameEn && a.Id != party.AccountId, ct);
                if (dupNameEn)
                    return Result.Failure<bool>("الاسم الإنجليزي مكرر في هذا النوع");
            }

            var phone = req.Phone?.Trim();
            if (!string.IsNullOrWhiteSpace(phone))
            {
                var dupPhone = await _db.FinancialParties.AnyAsync(p => p.Phone == phone && p.Id != party.Id, ct);
                if (dupPhone)
                    return Result.Failure<bool>("رقم الهاتف مكرر — مستخدَم لدى طرف آخر");
            }

            var email = req.Email?.Trim();
            if (!string.IsNullOrWhiteSpace(email))
            {
                var dupEmail = await _db.FinancialParties.AnyAsync(p => p.Email == email && p.Id != party.Id, ct);
                if (dupEmail)
                    return Result.Failure<bool>("البريد الإلكتروني مكرر — مستخدَم لدى طرف آخر");
            }

            // ‎تحديث الحساب أولاً (مصدر الاسم الوحيد).
            party.Account.UpdateBasic(nameAr, req.NameEn, party.Account.Description);

            var creditLimitsMap = req.CreditLimits?
                .ToDictionary(
                    kv => kv.Key,
                    kv => new CreditLimitEntry(kv.Value.Debit, kv.Value.Credit));

            party.Update(creditLimitsMap, req.AllowedCurrencies,
                req.Phone, req.Mobile, req.Email,
                req.Address, req.ContactPerson, req.Notes,
                req.BankAccountNumber, req.SwiftCode, req.AddressEn,
                party.Category.Kind.IsBankLike() ? req.CurrencyIbans : null);

            if (req.IsActive) party.Activate(); else party.Deactivate();

            await _db.SaveChangesAsync(ct);
            return Result.Success(true);
        }
        catch (DomainException ex) { return Result.Failure<bool>(ex.Message); }
    }
}
