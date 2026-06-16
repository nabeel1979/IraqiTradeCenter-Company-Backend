using IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.ManageAccounts;

public class PermanentlyDeleteAccountHandler : IRequestHandler<PermanentlyDeleteAccountCommand, Result>
{
    private readonly IAccountingDbContext _db;
    public PermanentlyDeleteAccountHandler(IAccountingDbContext db) { _db = db; }

    public async Task<Result> Handle(PermanentlyDeleteAccountCommand req, CancellationToken ct)
    {
        var account = await _db.Accounts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == req.Id, ct);
        if (account is null) return Result.Failure("الحساب غير موجود");
        if (!account.IsDeleted)
            return Result.Failure(
                "الحذف النهائي مسموح فقط للحسابات الموجودة في سلة المهملات. احذف الحساب أولاً.");

        if (await FinancialManagementAccountGuard.IsManagedAccountAsync(_db, req.Id, ct))
            return Result.Failure(FinancialManagementAccountGuard.ManagedAccountMessage);

        if (await _db.Accounts.IgnoreQueryFilters().AnyAsync(a => a.ParentId == req.Id, ct))
            return Result.Failure(
                "لا يمكن الحذف النهائي — للحساب فروع (سواء نشطة أو في السلة). احذفها نهائياً أولاً.");

        if (await CashBoxPartySource.IsCashBoxAccountAsync(_db, req.Id, ct))
            return Result.Failure("لا يمكن الحذف النهائي — الحساب مرتبط بصندوق.");

        if (await _db.JournalVoucherTypes.IgnoreQueryFilters().AnyAsync(
                v => v.DefaultDebitAccountId == req.Id || v.DefaultCreditAccountId == req.Id, ct))
            return Result.Failure("لا يمكن الحذف النهائي — الحساب مستخدم في نوع سند.");

        // حذف متسلسل: القيود التي تحتوي سطوراً لهذا الحساب → سطورها → مرفقاتها
        var affectedEntryIds = await _db.JournalEntryLines.IgnoreQueryFilters()
            .Where(l => l.AccountId == req.Id)
            .Select(l => l.JournalEntryId)
            .Distinct()
            .ToListAsync(ct);

        if (affectedEntryIds.Count > 0)
        {
            var attachments = await _db.VoucherAttachments.IgnoreQueryFilters()
                .Where(a => affectedEntryIds.Contains(a.JournalEntryId))
                .ToListAsync(ct);
            _db.VoucherAttachments.RemoveRange(attachments);

            var lines = await _db.JournalEntryLines.IgnoreQueryFilters()
                .Where(l => affectedEntryIds.Contains(l.JournalEntryId))
                .ToListAsync(ct);
            _db.JournalEntryLines.RemoveRange(lines);

            var entries = await _db.JournalEntries.IgnoreQueryFilters()
                .Where(e => affectedEntryIds.Contains(e.Id))
                .ToListAsync(ct);
            _db.JournalEntries.RemoveRange(entries);
        }

        _db.Accounts.Remove(account);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
