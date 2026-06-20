using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.SharedKernel.Models;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.API.Trash.Providers;

/// <summary>
/// مُزوِّد سلة الأطراف المالية (موردون / عملاء / مصارف).
/// يضمن أن الحذف النهائي يزيل الصف الفعلي من قاعدة البيانات،
/// ويشترط حذف الطرف قبل حسابه تفادياً لقيد FK.
/// </summary>
public class FinancialPartyTrashProvider : ITrashProvider
{
    private readonly IAccountingDbContext _db;
    public FinancialPartyTrashProvider(IAccountingDbContext db) { _db = db; }

    public string EntityType => "FinancialParty";

    public async Task<List<TrashItemDto>> ListAsync(CancellationToken ct)
    {
        var rows = await _db.FinancialParties.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IsDeleted)
            .Include(p => p.Category)
            .Include(p => p.Account)
            .OrderByDescending(p => p.DeletedAt)
            .ToListAsync(ct);

        return rows.Select(p =>
        {
            var kindLabel = p.Category.Kind switch
            {
                FinancialPartyKind.Supplier       => "مورد",
                FinancialPartyKind.Customer        => "عميل",
                FinancialPartyKind.Bank            => "مصرف",
                FinancialPartyKind.CashBox         => "صندوق",
                FinancialPartyKind.PaymentCompany  => "شركة دفع",
                _                                  => p.Category.Kind.ToString(),
            };
            return new TrashItemDto
            {
                EntityType      = EntityType,
                EntityTypeLabel = kindLabel,
                Module          = "الإدارة المالية",
                Icon            = "Users",
                EntityId        = p.Id,
                Code            = p.Account?.Code,
                DisplayName     = p.Account?.NameAr ?? $"طرف #{p.Id}",
                SubInfo         = $"{kindLabel} · {p.Category.NameAr}",
                DeletedAt       = p.DeletedAt,
                DeletedBy       = p.UpdatedBy,
            };
        }).ToList();
    }

    public async Task<Result> RestoreAsync(int id, CancellationToken ct)
    {
        var party = await _db.FinancialParties.IgnoreQueryFilters()
            .Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (party is null) return Result.Failure("الطرف المالي غير موجود");
        if (!party.IsDeleted) return Result.Failure("الطرف ليس في السلة");

        // إذا كان الحساب الأب في السلة فلا يمكن الاستعادة.
        if (party.Account is { IsDeleted: true })
        {
            // نحاول استعادة الحساب أيضاً إن كان مرتبطاً بهذا الطرف فقط
            var accountShared = await _db.FinancialParties.IgnoreQueryFilters()
                .AnyAsync(p => p.AccountId == party.AccountId && p.Id != id, ct);
            if (!accountShared)
                party.Account.Restore();
            else
                return Result.Failure($"الحساب ({party.Account.Code}) محذوف ومشترك — استعده من سلة الحسابات أولاً");
        }

        party.Restore();
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> PermanentlyDeleteAsync(int id, CancellationToken ct)
    {
        var party = await _db.FinancialParties.IgnoreQueryFilters()
            .Include(p => p.Account)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (party is null) return Result.Failure("الطرف المالي غير موجود");
        if (!party.IsDeleted) return Result.Failure("الحذف النهائي مسموح فقط من السلة");

        // التحقق من عدم استخدام الحساب في قيود.
        var inUse = await _db.JournalEntryLines
            .AnyAsync(l => l.AccountId == party.AccountId, ct);
        if (inUse)
            return Result.Failure("لا يمكن الحذف النهائي — الحساب مرتبط بقيود محاسبية");

        // حذف الطرف أولاً ثم الحساب (ترتيب FK).
        _db.FinancialParties.Remove(party);
        await _db.SaveChangesAsync(ct);

        // حذف الحساب إذا لم يعد مرتبطاً بأي طرف آخر.
        if (party.Account is not null)
        {
            var accountStillUsed = await _db.FinancialParties.IgnoreQueryFilters()
                .AnyAsync(p => p.AccountId == party.AccountId, ct);
            if (!accountStillUsed)
            {
                var account = await _db.Accounts.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(a => a.Id == party.AccountId, ct);
                if (account is not null)
                {
                    _db.Accounts.Remove(account);
                    await _db.SaveChangesAsync(ct);
                }
            }
        }

        return Result.Success();
    }
}
