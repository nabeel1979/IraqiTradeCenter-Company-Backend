using IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.CashBoxes;

internal static class CashBoxLegacyCrud
{
    internal const string UseFinancialManagementMessage =
        "تم نقل إدارة الصناديق إلى الإدارة المالية — استخدم تبويب «الصناديق» في صفحة الإدارة المالية.";
}

// ─────────────────────────────────────────────────────────────────────
// DTOs
// ─────────────────────────────────────────────────────────────────────

public record CashBoxCurrencyDto(
    int Id,
    string Currency,
    decimal? DebitLimit,
    decimal? CreditLimit,
    bool IsActive
);

public record CashBoxDto(
    int Id,
    string Code,
    string NameAr,
    string? NameEn,
    string? Description,
    int AccountId,
    string? AccountCode,
    string? AccountName,
    bool IsActive,
    int DisplayOrder,
    List<CashBoxCurrencyDto> Currencies,
    bool HasMovements
);

public record UpsertCashBoxCurrencyDto(
    string Currency,
    decimal? DebitLimit,
    decimal? CreditLimit,
    bool IsActive
);

public record UpsertCashBoxDto(
    string Code,
    string NameAr,
    string? NameEn,
    string? Description,
    int AccountId,
    bool IsActive,
    int DisplayOrder,
    List<UpsertCashBoxCurrencyDto> Currencies
);

// ─────────────────────────────────────────────────────────────────────
// Queries
// ─────────────────────────────────────────────────────────────────────

public record GetCashBoxesQuery(bool? ActiveOnly = null) : IRequest<List<CashBoxDto>>;

public class GetCashBoxesHandler : IRequestHandler<GetCashBoxesQuery, List<CashBoxDto>>
{
    private readonly IAccountingDbContext _db;
    public GetCashBoxesHandler(IAccountingDbContext db) => _db = db;

    public async Task<List<CashBoxDto>> Handle(GetCashBoxesQuery req, CancellationToken ct)
    {
        var rows = await CashBoxPartySource.GetAllAsync(_db, req.ActiveOnly, ct);

        var accountIds = rows.Select(r => r.AccountId).Distinct().ToList();
        var accountsWithMovements = accountIds.Count == 0
            ? new HashSet<int>()
            : (await _db.JournalEntryLines.AsNoTracking()
                .Where(l => accountIds.Contains(l.AccountId))
                .Select(l => l.AccountId)
                .Distinct()
                .ToListAsync(ct)).ToHashSet();

        return rows
            .Select(x => CashBoxPartySource.ToCashBoxDto(x, accountsWithMovements.Contains(x.AccountId)))
            .ToList();
    }
}

public record GetCashBoxByIdQuery(int Id) : IRequest<CashBoxDto?>;

public class GetCashBoxByIdHandler : IRequestHandler<GetCashBoxByIdQuery, CashBoxDto?>
{
    private readonly IAccountingDbContext _db;
    public GetCashBoxByIdHandler(IAccountingDbContext db) => _db = db;

    public async Task<CashBoxDto?> Handle(GetCashBoxByIdQuery req, CancellationToken ct)
    {
        var x = await CashBoxPartySource.GetByIdAsync(_db, req.Id, ct);
        if (x == null) return null;

        var hasMovements = await _db.JournalEntryLines.AsNoTracking()
            .AnyAsync(l => l.AccountId == x.AccountId, ct);

        return CashBoxPartySource.ToCashBoxDto(x, hasMovements);
    }
}

// ─────────────────────────────────────────────────────────────────────
// Commands
// ─────────────────────────────────────────────────────────────────────

public record CreateCashBoxCommand(UpsertCashBoxDto Data) : IRequest<int>;

public class CreateCashBoxHandler : IRequestHandler<CreateCashBoxCommand, int>
{
    private readonly IAccountingDbContext _db;
    public CreateCashBoxHandler(IAccountingDbContext db) => _db = db;

    public Task<int> Handle(CreateCashBoxCommand req, CancellationToken ct) =>
        throw new DomainException(CashBoxLegacyCrud.UseFinancialManagementMessage);

    internal static async Task ValidateAccountAsync(IAccountingDbContext db, int accountId, CancellationToken ct)
    {
        var ok = await db.Accounts.AsNoTracking().AnyAsync(a => a.Id == accountId && a.IsLeaf && a.IsActive, ct);
        if (!ok) throw new DomainException("حساب الصندوق غير صالح (يجب أن يكون فرعياً مفعّلاً)");
    }

    internal static void ValidateCurrencies(IEnumerable<UpsertCashBoxCurrencyDto>? items)
    {
        if (items == null) return;
        var dups = items
            .Select(c => (c.Currency ?? string.Empty).Trim().ToUpperInvariant())
            .GroupBy(c => c)
            .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (dups.Any())
            throw new DomainException($"عملات مكرّرة في الصندوق: {string.Join(", ", dups)}");
    }
}

public record UpdateCashBoxCommand(int Id, UpsertCashBoxDto Data) : IRequest<Unit>;

public class UpdateCashBoxHandler : IRequestHandler<UpdateCashBoxCommand, Unit>
{
    private readonly IAccountingDbContext _db;
    public UpdateCashBoxHandler(IAccountingDbContext db) => _db = db;

    public Task<Unit> Handle(UpdateCashBoxCommand req, CancellationToken ct) =>
        throw new DomainException(CashBoxLegacyCrud.UseFinancialManagementMessage);
}

public record ToggleCashBoxCommand(int Id, bool IsActive) : IRequest<Unit>;

public class ToggleCashBoxHandler : IRequestHandler<ToggleCashBoxCommand, Unit>
{
    private readonly IAccountingDbContext _db;
    public ToggleCashBoxHandler(IAccountingDbContext db) => _db = db;

    public Task<Unit> Handle(ToggleCashBoxCommand req, CancellationToken ct) =>
        throw new DomainException(CashBoxLegacyCrud.UseFinancialManagementMessage);
}

public record DeleteCashBoxCommand(int Id) : IRequest<Unit>;

public class DeleteCashBoxHandler : IRequestHandler<DeleteCashBoxCommand, Unit>
{
    private readonly IAccountingDbContext _db;
    public DeleteCashBoxHandler(IAccountingDbContext db) => _db = db;

    public Task<Unit> Handle(DeleteCashBoxCommand req, CancellationToken ct) =>
        throw new DomainException(CashBoxLegacyCrud.UseFinancialManagementMessage);
}

public record MoveCashBoxCommand(int Id, string Direction) : IRequest<Unit>;

public class MoveCashBoxHandler : IRequestHandler<MoveCashBoxCommand, Unit>
{
    private readonly IAccountingDbContext _db;
    public MoveCashBoxHandler(IAccountingDbContext db) => _db = db;

    public Task<Unit> Handle(MoveCashBoxCommand req, CancellationToken ct) =>
        throw new DomainException(CashBoxLegacyCrud.UseFinancialManagementMessage);
}
