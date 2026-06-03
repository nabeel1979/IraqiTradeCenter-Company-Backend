using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageFinancialPartyCategories;

public class CreateFinancialPartyCategoryHandler
    : IRequestHandler<CreateFinancialPartyCategoryCommand, Result<int>>
{
    private readonly IAccountingDbContext _db;
    public CreateFinancialPartyCategoryHandler(IAccountingDbContext db) => _db = db;

    public async Task<Result<int>> Handle(CreateFinancialPartyCategoryCommand req, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.NameAr))
                return Result.Failure<int>("اسم النوع مطلوب");

            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == req.MainAccountId, ct);
            if (account is null)
                return Result.Failure<int>("الحساب الرئيسي غير موجود");

            if (!account.IsLeaf)
                return Result.Failure<int>(
                    "يجب أن يكون الحساب ورقة (ليس له أبناء) — اختر حساباً فرعياً ليس له تفرعات");

            var hasJournalLines = await _db.JournalEntryLines
                .AnyAsync(l => l.AccountId == req.MainAccountId, ct);
            if (hasJournalLines)
                return Result.Failure<int>(
                    "هذا الحساب مرتبط بقيود محاسبية — اختر حساباً لم يُستخدم في أي قيد");

            if (account.IsLockedForParties)
                return Result.Failure<int>(
                    "هذا الحساب محجوز بالفعل لنوع آخر — اختر حساباً مختلفاً");

            var existingForAccount = await _db.FinancialPartyCategories
                .AnyAsync(c => c.MainAccountId == req.MainAccountId, ct);
            if (existingForAccount)
                return Result.Failure<int>(
                    "هذا الحساب مرتبط بنوع موجود بالفعل — اختر حساباً مختلفاً");

            var dupName = await _db.FinancialPartyCategories
                .AnyAsync(c => c.Kind == req.Kind && c.NameAr == req.NameAr.Trim(), ct);
            if (dupName)
                return Result.Failure<int>("يوجد نوع بنفس الاسم — استخدم اسماً مختلفاً");

            var maxOrder = await _db.FinancialPartyCategories
                .Where(c => c.Kind == req.Kind)
                .Select(c => (int?)c.DisplayOrder)
                .MaxAsync(ct) ?? 0;

            account.LockForParties();

            var category = FinancialPartyCategory.Create(
                req.Kind, req.NameAr, req.NameEn, req.MainAccountId, maxOrder + 10);

            await _db.FinancialPartyCategories.AddAsync(category, ct);
            await _db.SaveChangesAsync(ct);
            return Result.Success(category.Id);
        }
        catch (DomainException ex) { return Result.Failure<int>(ex.Message); }
    }
}
