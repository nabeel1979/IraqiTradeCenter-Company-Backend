using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialParties;

public class CreateFinancialPartyHandler
    : IRequestHandler<CreateFinancialPartyCommand, Result<int>>
{
    private readonly IAccountingDbContext _db;
    public CreateFinancialPartyHandler(IAccountingDbContext db) => _db = db;

    public async Task<Result<int>> Handle(CreateFinancialPartyCommand req, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.NameAr))
                return Result.Failure<int>("اسم الطرف مطلوب");

            var category = await _db.FinancialPartyCategories
                .Include(c => c.MainAccount)
                .FirstOrDefaultAsync(c => c.Id == req.CategoryId, ct);
            if (category is null)
                return Result.Failure<int>("النوع غير موجود");
            if (!category.IsActive)
                return Result.Failure<int>("النوع غير مفعَّل");

            var nameAr = req.NameAr.Trim();
            var nameEn = req.NameEn?.Trim();

            // ‎منع التكرار (إدخال يدوي أو استيراد): الاسم العربي/الإنجليزي ضمن نفس النوع،
            // ‎والهاتف/البريد عبر كل الأطراف.
            var dupNameAr = await _db.Accounts
                .AnyAsync(a => a.ParentId == category.MainAccountId && a.NameAr == nameAr, ct);
            if (dupNameAr)
                return Result.Failure<int>("الاسم العربي مكرر في هذا النوع — استخدم اسماً مختلفاً");

            if (!string.IsNullOrWhiteSpace(nameEn))
            {
                var dupNameEn = await _db.Accounts
                    .AnyAsync(a => a.ParentId == category.MainAccountId && a.NameEn == nameEn, ct);
                if (dupNameEn)
                    return Result.Failure<int>("الاسم الإنجليزي مكرر في هذا النوع — استخدم اسماً مختلفاً");
            }

            var phone = req.Phone?.Trim();
            if (!string.IsNullOrWhiteSpace(phone))
            {
                var dupPhone = await _db.FinancialParties.AnyAsync(p => p.Phone == phone, ct);
                if (dupPhone)
                    return Result.Failure<int>("رقم الهاتف مكرر — مستخدَم لدى طرف آخر");
            }

            var email = req.Email?.Trim();
            if (!string.IsNullOrWhiteSpace(email))
            {
                var dupEmail = await _db.FinancialParties.AnyAsync(p => p.Email == email, ct);
                if (dupEmail)
                    return Result.Failure<int>("البريد الإلكتروني مكرر — مستخدَم لدى طرف آخر");
            }

            // توليد كود حساب فريد: {كود_الحساب_الرئيسي}_{6 أرقام عشوائية}
            var parentCode = category.MainAccount.Code;
            var accountCode = await GenerateUniqueCodeAsync(parentCode, ct);

            var parentAccount = category.MainAccount;
            var level = parentAccount.Level + 1;
            if (level > 5)
                return Result.Failure<int>("شجرة الحسابات بلغت أقصى عمق مسموح (5 مستويات)");

            if (parentAccount.IsLeaf)
                parentAccount.MarkAsLeaf(false);

            var account = Account.Create(
                accountCode, nameAr,
                parentAccount.Type, parentAccount.Nature,
                parentAccount.Id, level, true);

            if (!string.IsNullOrWhiteSpace(req.NameEn))
                account.UpdateBasic(nameAr, req.NameEn, null);

            await _db.Accounts.AddAsync(account, ct);
            await _db.SaveChangesAsync(ct);

            // ‎خرائط Dto → Domain للسقوف الائتمانية
            var creditLimitsMap = req.CreditLimits?
                .ToDictionary(
                    kv => kv.Key,
                    kv => new CreditLimitEntry(kv.Value.Debit, kv.Value.Credit));

            var party = FinancialParty.Create(
                req.CategoryId, account.Id,
                creditLimitsMap, req.AllowedCurrencies,
                req.Phone, req.Mobile, req.Email,
                req.Address, req.ContactPerson, req.Notes,
                req.BankAccountNumber, req.SwiftCode, req.AddressEn,
                category.Kind.IsBankLike() ? req.CurrencyIbans : null);

            await _db.FinancialParties.AddAsync(party, ct);
            await _db.SaveChangesAsync(ct);
            return Result.Success(party.Id);
        }
        catch (DomainException ex) { return Result.Failure<int>(ex.Message); }
    }

    private async Task<string> GenerateUniqueCodeAsync(string parentCode, CancellationToken ct)
    {
        // ‎الكود ينتج بصيغة: {كود_الحساب_الأب}{6 أرقام عشوائية} بدون فواصل،
        // ‎كي يُعرض كرقم متَّصل وأن يتدفق بشكل صحيح في الـ RTL.
        var rng = Random.Shared;
        string code;
        int attempts = 0;
        do
        {
            var digits = rng.Next(100000, 999999).ToString();
            code = $"{parentCode}{digits}";
            attempts++;
            if (attempts > 100)
                throw new DomainException("تعذَّر توليد كود حساب فريد — يُرجى المحاولة مرة أخرى");
        }
        while (await _db.Accounts.IgnoreQueryFilters().AnyAsync(a => a.Code == code, ct));

        return code;
    }
}
