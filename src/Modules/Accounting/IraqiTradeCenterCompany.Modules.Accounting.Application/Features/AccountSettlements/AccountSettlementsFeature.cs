using System.Text.Json;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Features.GetAccountBalances;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.AccountSettlements;

public record AccountSettlementSettingsDto(
    Dictionary<string, int> TransitAccounts,
    int? FxGainAccountId,
    int? FxLossAccountId,
    int? FxDiscountAccountId);

public record AccountSettlementRowDto(
    int Id,
    string SettlementNumber,
    DateTime SettlementDate,
    int SourceAccountId,
    string SourceAccountCode,
    string SourceAccountName,
    string SourceCurrency,
    decimal SourceAmount,
    int TargetAccountId,
    string TargetAccountCode,
    string TargetAccountName,
    string TargetCurrency,
    decimal TargetAmount,
    decimal ExchangeRate,
    decimal FxGainLossAmount,
    decimal FxDiscountAmount,
    decimal EffectiveFxGainLossAmount,
    bool IsCancelled,
    string? CancelReason,
    int SourceJournalEntryId,
    string? SourceEntryNumber,
    int TargetJournalEntryId,
    string? TargetEntryNumber,
    int? SourceReversalJournalEntryId,
    int? TargetReversalJournalEntryId,
    string? SourceReversalEntryNumber,
    string? TargetReversalEntryNumber,
    string? Description,
    DateTime CreatedAt);

public record SettlementTransitMovementDto(
    int SettlementId,
    string SettlementNumber,
    DateTime SettlementDate,
    int TransitAccountId,
    string TransitAccountCode,
    string TransitAccountName,
    string Currency,
    bool IsDebit,
    decimal Amount,
    string Side,
    bool IsCancelled,
    int JournalEntryId,
    string? EntryNumber);

public record SettlementPreviewDto(
    decimal SourceBalance,
    decimal TargetBalance,
    decimal BulletinCrossRate,
    decimal ComputedTargetAmount,
    decimal FxGainLossAmount,
    decimal FxDiscountAmount,
    decimal EffectiveFxGainLossAmount,
    string BaseCurrency,
    string? BulletinName,
    DateTime? BulletinEffectiveAt,
    string ExchangeRateDisplay,
    string BulletinCrossRateDisplay);

public record SettlementJournalLinePreviewDto(
    int AccountId,
    string AccountCode,
    string AccountName,
    bool IsDebit,
    decimal Amount,
    string Currency,
    string? Description);

public record SettlementCreatePreviewDto(
    SettlementPreviewDto Preview,
    List<SettlementJournalLinePreviewDto> SourceEntryLines,
    List<SettlementJournalLinePreviewDto> TargetEntryLines);

public record GetAccountSettlementSettingsQuery : IRequest<AccountSettlementSettingsDto>;

public class GetAccountSettlementSettingsHandler : IRequestHandler<GetAccountSettlementSettingsQuery, AccountSettlementSettingsDto>
{
    private readonly IAccountingDbContext _db;
    public GetAccountSettlementSettingsHandler(IAccountingDbContext db) => _db = db;

    public async Task<AccountSettlementSettingsDto> Handle(GetAccountSettlementSettingsQuery req, CancellationToken ct)
    {
        var s = await _db.AccountSettlementSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == AccountSettlementSettings.SingletonId, ct);
        return MapSettings(s);
    }

    internal static AccountSettlementSettingsDto MapSettings(AccountSettlementSettings? s)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(s?.TransitAccountsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(s.TransitAccountsJson);
                if (parsed != null)
                    foreach (var kv in parsed)
                        if (kv.Value > 0) map[kv.Key.Trim().ToUpperInvariant()] = kv.Value;
            }
            catch { /* ignore malformed json */ }
        }
        return new AccountSettlementSettingsDto(map, s?.FxGainAccountId, s?.FxLossAccountId, s?.FxDiscountAccountId);
    }
}

public record UpdateAccountSettlementSettingsCommand(
    Dictionary<string, int> TransitAccounts,
    int? FxGainAccountId,
    int? FxLossAccountId,
    int? FxDiscountAccountId) : IRequest<Result>;

public class UpdateAccountSettlementSettingsHandler : IRequestHandler<UpdateAccountSettlementSettingsCommand, Result>
{
    private readonly IAccountingDbContext _db;
    public UpdateAccountSettlementSettingsHandler(IAccountingDbContext db) => _db = db;

    public async Task<Result> Handle(UpdateAccountSettlementSettingsCommand req, CancellationToken ct)
    {
        await _db.EnsureAccountSettlementSettingsRowAsync(ct);

        var s = await _db.AccountSettlementSettings
            .FirstOrDefaultAsync(x => x.Id == AccountSettlementSettings.SingletonId, ct);
        if (s is null)
            return Result.Failure("تعذّر تهيئة إعدادات التسوية");

        var transit = req.TransitAccounts ?? new Dictionary<string, int>();
        var clean = transit
            .Where(kv => kv.Value > 0 && !string.IsNullOrWhiteSpace(kv.Key))
            .ToDictionary(kv => kv.Key.Trim().ToUpperInvariant(), kv => kv.Value);

        foreach (var accId in clean.Values.Distinct())
        {
            var acc = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accId, ct);
            if (acc is null) return Result.Failure($"الحساب الوسيط #{accId} غير موجود");
            if (!acc.IsLeaf) return Result.Failure($"الحساب {acc.Code} ليس حساباً فرعياً");
            acc.SetExcludedFromReports(true);
        }

        s.Update(clean.Count > 0 ? JsonSerializer.Serialize(clean) : null,
            req.FxGainAccountId, req.FxLossAccountId, req.FxDiscountAccountId);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public record GetAccountSettlementsQuery(DateTime? From, DateTime? To) : IRequest<List<AccountSettlementRowDto>>;

public class GetAccountSettlementsHandler : IRequestHandler<GetAccountSettlementsQuery, List<AccountSettlementRowDto>>
{
    private readonly IAccountingDbContext _db;
    public GetAccountSettlementsHandler(IAccountingDbContext db) => _db = db;

    public async Task<List<AccountSettlementRowDto>> Handle(GetAccountSettlementsQuery req, CancellationToken ct)
    {
        var q = _db.AccountSettlements.IgnoreQueryFilters().AsNoTracking().AsQueryable();
        if (req.From.HasValue) q = q.Where(x => x.SettlementDate >= req.From.Value.Date);
        if (req.To.HasValue) q = q.Where(x => x.SettlementDate <= req.To.Value.Date.AddDays(1).AddTicks(-1));

        var rows = await q.OrderByDescending(x => x.SettlementDate).ThenByDescending(x => x.Id).Take(500).ToListAsync(ct);
        return await MapRowsAsync(rows, _db, ct);
    }

    internal static async Task<List<AccountSettlementRowDto>> MapRowsAsync(
        List<AccountSettlement> rows, IAccountingDbContext db, CancellationToken ct)
    {
        if (rows.Count == 0) return new List<AccountSettlementRowDto>();
        var accIds = rows.SelectMany(r => new[] { r.SourceAccountId, r.TargetAccountId }).Distinct().ToList();
        var accMap = await db.Accounts.AsNoTracking()
            .Where(a => accIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => (a.Code, a.NameAr), ct);
        var jeIds = rows.SelectMany(r => new[] {
            r.SourceJournalEntryId, r.TargetJournalEntryId,
            r.SourceReversalJournalEntryId, r.TargetReversalJournalEntryId,
        }).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var jeMap = await db.JournalEntries.AsNoTracking()
            .Where(j => jeIds.Contains(j.Id))
            .ToDictionaryAsync(j => j.Id, j => j.EntryNumber, ct);

        return rows.Select(r =>
        {
            accMap.TryGetValue(r.SourceAccountId, out var sa);
            accMap.TryGetValue(r.TargetAccountId, out var ta);
            jeMap.TryGetValue(r.SourceJournalEntryId, out var sen);
            jeMap.TryGetValue(r.TargetJournalEntryId, out var ten);
            string? srcRevNum = null, tgtRevNum = null;
            if (r.SourceReversalJournalEntryId.HasValue)
                jeMap.TryGetValue(r.SourceReversalJournalEntryId.Value, out srcRevNum);
            if (r.TargetReversalJournalEntryId.HasValue)
                jeMap.TryGetValue(r.TargetReversalJournalEntryId.Value, out tgtRevNum);
            return new AccountSettlementRowDto(
                r.Id, r.SettlementNumber, r.SettlementDate,
                r.SourceAccountId, sa.Code ?? "", sa.NameAr ?? "", r.SourceCurrency, r.SourceAmount,
                r.TargetAccountId, ta.Code ?? "", ta.NameAr ?? "", r.TargetCurrency, r.TargetAmount,
                r.ExchangeRate, r.FxGainLossAmount, r.FxDiscountAmount,
                SettlementEngine.EffectiveFx(r.FxGainLossAmount, r.FxDiscountAmount),
                r.IsDeleted, r.CancelReason,
                r.SourceJournalEntryId, sen, r.TargetJournalEntryId, ten,
                r.SourceReversalJournalEntryId, r.TargetReversalJournalEntryId,
                srcRevNum, tgtRevNum,
                r.Description, r.CreatedAt);
        }).ToList();
    }
}

public record PreviewAccountSettlementQuery(
    int SourceAccountId,
    string SourceCurrency,
    decimal SourceAmount,
    int TargetAccountId,
    string TargetCurrency,
    decimal? TargetAmount,
    decimal? ExchangeRate,
    decimal? FxDiscountAmount,
    DateTime SettlementDate) : IRequest<Result<SettlementCreatePreviewDto>>;

public class PreviewAccountSettlementHandler : IRequestHandler<PreviewAccountSettlementQuery, Result<SettlementCreatePreviewDto>>
{
    private readonly IAccountingDbContext _db;
    private readonly IMediator _mediator;
    public PreviewAccountSettlementHandler(IAccountingDbContext db, IMediator mediator)
    {
        _db = db;
        _mediator = mediator;
    }

    public async Task<Result<SettlementCreatePreviewDto>> Handle(PreviewAccountSettlementQuery req, CancellationToken ct)
    {
        try
        {
            var built = await SettlementEngine.BuildAsync(_db, _mediator, req, previewOnly: true, ct);
            return Result.Success(built.PreviewDto);
        }
        catch (DomainException ex) { return Result.Failure<SettlementCreatePreviewDto>(ex.Message); }
    }
}

public record CreateAccountSettlementCommand(
    int SourceAccountId,
    string SourceCurrency,
    decimal SourceAmount,
    int TargetAccountId,
    string TargetCurrency,
    decimal? TargetAmount,
    decimal? ExchangeRate,
    decimal? FxDiscountAmount,
    DateTime SettlementDate,
    int? SourceTransitAccountId,
    int? TargetTransitAccountId,
    string? Description) : IRequest<Result<int>>;

public class CreateAccountSettlementHandler : IRequestHandler<CreateAccountSettlementCommand, Result<int>>
{
    private readonly IAccountingDbContext _db;
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodResolver _periods;

    public CreateAccountSettlementHandler(
        IAccountingDbContext db, IMediator mediator, ICurrentUserService currentUser, IPeriodResolver periods)
    {
        _db = db;
        _mediator = mediator;
        _currentUser = currentUser;
        _periods = periods;
    }

    public async Task<Result<int>> Handle(CreateAccountSettlementCommand req, CancellationToken ct)
    {
        var previewReq = new PreviewAccountSettlementQuery(
            req.SourceAccountId, req.SourceCurrency, req.SourceAmount,
            req.TargetAccountId, req.TargetCurrency, req.TargetAmount, req.ExchangeRate,
            req.FxDiscountAmount, req.SettlementDate);

        SettlementEngine.BuildResult built;
        try
        {
            built = await SettlementEngine.BuildAsync(_db, _mediator, previewReq, previewOnly: false, ct,
                sourceTransitOverride: req.SourceTransitAccountId,
                targetTransitOverride: req.TargetTransitAccountId);
        }
        catch (DomainException ex)
        {
            return Result.Failure<int>(ex.Message);
        }

        await using var tx = await _db.BeginTransactionAsync(ct);
        try
        {
            var vtOut = await EnsureVoucherTypeAsync("AS-OUT", "تسوية — قيد مصدر", ct);
            var vtIn = await EnsureVoucherTypeAsync("AS-IN", "تسوية — قيد هدف", ct);

            var refNum = await NextSettlementNumberAsync(ct);
            var desc = string.IsNullOrWhiteSpace(req.Description)
                ? $"تسوية حسابات {refNum}"
                : req.Description!.Trim();

            var srcEntry = await BuildJournalEntryAsync(
                req.SettlementDate, desc, vtOut.Id, built.SourceCurrency,
                built.SourceLines, "AccountSettlement", refNum, ct);

            var tgtEntry = await BuildJournalEntryAsync(
                req.SettlementDate, desc, vtIn.Id, built.TargetCurrency,
                built.TargetLines, "AccountSettlement", refNum, ct);

            var settlement = AccountSettlement.Create(
                refNum,
                req.SourceAccountId, built.SourceCurrency, req.SourceAmount,
                req.TargetAccountId, built.TargetCurrency, built.TargetAmount,
                built.ExchangeRate,
                built.SourceTransitAccountId, built.TargetTransitAccountId,
                built.FxGainLossAmount, built.FxGainLossAccountId,
                built.FxDiscountAmount, built.FxDiscountAccountId,
                req.SettlementDate,
                srcEntry.Id, tgtEntry.Id,
                desc);

            await _db.AccountSettlements.AddAsync(settlement, ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Result.Success(settlement.Id);
        }
        catch (DomainException ex)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<int>(ex.Message);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            var msg = ex.InnerException?.Message ?? ex.Message;
            return Result.Failure<int>($"تعذّر اعتماد التسوية: {msg}");
        }
    }

    private async Task<JournalVoucherType> EnsureVoucherTypeAsync(string code, string nameAr, CancellationToken ct)
    {
        var existing = await _db.JournalVoucherTypes.IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Code == code, ct);
        if (existing != null)
        {
            if (existing.IsDeleted) existing.Restore();
            if (!existing.IsEnabled) existing.SetEnabled(true);
            return existing;
        }
        var vt = JournalVoucherType.Create(code, nameAr, description: "نوع سند نظامي لتسوية الحسابات",
            isEnabled: true, isSystem: true, displayOrder: 820, nature: VoucherNature.Mixed, showInSidebar: false);
        await _db.JournalVoucherTypes.AddAsync(vt, ct);
        await _db.SaveChangesAsync(ct);
        return vt;
    }

    private async Task<string> NextSettlementNumberAsync(CancellationToken ct)
    {
        var nums = await _db.AccountSettlements.IgnoreQueryFilters()
            .Where(s => s.SettlementNumber.StartsWith("STL-"))
            .Select(s => s.SettlementNumber).ToListAsync(ct);
        var max = 0;
        foreach (var n in nums)
            if (n.Length > 4 && int.TryParse(n[4..], out var v) && v > max) max = v;
        return $"STL-{max + 1}";
    }

    private async Task<JournalEntry> BuildJournalEntryAsync(
        DateTime date, string description, int voucherTypeId, string currency,
        IReadOnlyList<(int AccountId, bool IsDebit, decimal Amount, string? LineDesc)> lines,
        string refType, string refNumber, CancellationToken ct)
    {
        SettlementEngine.EnsureLinesBalanced(lines);

        var cashBoxCheck = await CashBoxGuard.ValidateAsync(
            _db,
            lines.Select(l => new CashBoxGuard.LineSnapshot(l.AccountId, l.IsDebit, l.Amount)).ToList(),
            currency,
            voucherTypeId,
            excludeJournalEntryId: null,
            ct,
            allowTransitAccounts: true);
        if (cashBoxCheck != null)
            throw new DomainException(cashBoxCheck);

        var (fyId, periodId) = await _periods.ResolveAsync(date, ct);
        var nextNum = await _db.GetNextJournalEntryNumberAsync(fyId, ct);
        var voucherSeq = await _db.GetNextVoucherSequenceAsync(voucherTypeId, fyId, ct);

        var entry = JournalEntry.Create(
            date, fyId, periodId, JournalEntrySource.System, description,
            refType, null, refNumber, JournalEntryType.Normal, currency,
            nextNum.ToString(), voucherTypeId, voucherSeq);

        foreach (var l in lines)
        {
            if (l.IsDebit) entry.AddDebit(l.AccountId, l.Amount, l.LineDesc);
            else entry.AddCredit(l.AccountId, l.Amount, l.LineDesc);
        }
        entry.Post(_currentUser.UserId?.ToString() ?? "system");
        await _db.JournalEntries.AddAsync(entry, ct);
        await _db.SaveChangesAsync(ct);
        return entry;
    }
}

public record CancelAccountSettlementDto(DateTime? ReversalDate, string? Reason);

public record CancelAccountSettlementCommand(int SettlementId, CancelAccountSettlementDto Data)
    : IRequest<Result<int>>;

public class CancelAccountSettlementHandler : IRequestHandler<CancelAccountSettlementCommand, Result<int>>
{
    private readonly IAccountingDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IPeriodResolver _periods;

    public CancelAccountSettlementHandler(
        IAccountingDbContext db, ICurrentUserService currentUser, IPeriodResolver periods)
    {
        _db = db;
        _currentUser = currentUser;
        _periods = periods;
    }

    public async Task<Result<int>> Handle(CancelAccountSettlementCommand req, CancellationToken ct)
    {
        try
        {
            var settlement = await _db.AccountSettlements.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == req.SettlementId, ct);
            if (settlement == null) return Result.Failure<int>("التسوية غير موجودة");
            if (settlement.IsDeleted) return Result.Failure<int>("التسوية ملغاة مسبقاً");

            var srcEntry = await _db.JournalEntries.Include(e => e.Lines)
                .FirstOrDefaultAsync(e => e.Id == settlement.SourceJournalEntryId, ct);
            var tgtEntry = await _db.JournalEntries.Include(e => e.Lines)
                .FirstOrDefaultAsync(e => e.Id == settlement.TargetJournalEntryId, ct);
            if (srcEntry == null || tgtEntry == null)
                return Result.Failure<int>("قيود التسوية غير موجودة");
            if (srcEntry.Status == JournalEntryStatus.Reversed || tgtEntry.Status == JournalEntryStatus.Reversed)
                return Result.Failure<int>("قيود التسوية معكوسة مسبقاً");

            var reversalDate = req.Data.ReversalDate ?? DateTime.Now;
            if (reversalDate.Date < settlement.SettlementDate.Date)
                reversalDate = settlement.SettlementDate;

            await using var tx = await _db.BeginTransactionAsync(ct);

            var reason = string.IsNullOrWhiteSpace(req.Data.Reason)
                ? $"إلغاء تسوية {settlement.SettlementNumber}"
                : req.Data.Reason!.Trim();
            var userId = _currentUser.UserId?.ToString() ?? "system";

            var srcRev = await ReverseEntryAsync(srcEntry, reversalDate, reason, settlement.SettlementNumber, ct);
            var tgtRev = await ReverseEntryAsync(tgtEntry, reversalDate, reason, settlement.SettlementNumber, ct);

            settlement.MarkAsCancelled(reason, srcRev.Id, tgtRev.Id, userId);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Result.Success(settlement.Id);
        }
        catch (DomainException ex) { return Result.Failure<int>(ex.Message); }
    }

    private async Task<JournalEntry> ReverseEntryAsync(
        JournalEntry original, DateTime date, string reason, string settlementNumber, CancellationToken ct)
    {
        if (original.Status != JournalEntryStatus.Posted)
            throw new DomainException($"لا يمكن عكس قيد غير مرحّل ({original.EntryNumber})");

        var (fyId, periodId) = await _periods.ResolveAsync(date, ct);
        var nextNum = await _db.GetNextJournalEntryNumberAsync(fyId, ct);
        int? voucherSeq = null;
        if (original.VoucherTypeId.HasValue)
            voucherSeq = await _db.GetNextVoucherSequenceAsync(original.VoucherTypeId.Value, fyId, ct);

        var rev = JournalEntry.Create(
            date, fyId, periodId, JournalEntrySource.System,
            $"عكس تسوية {settlementNumber} — {reason}",
            "AccountSettlementReversal", original.Id, settlementNumber,
            JournalEntryType.Normal, original.Currency,
            nextNum.ToString(), original.VoucherTypeId, voucherSeq);

        foreach (var l in original.Lines)
        {
            if (l.IsDebit) rev.AddCredit(l.AccountId, l.Amount, $"عكس: {l.Description}");
            else rev.AddDebit(l.AccountId, l.Amount, $"عكس: {l.Description}");
        }
        rev.Post(_currentUser.UserId?.ToString() ?? "system");
        await _db.JournalEntries.AddAsync(rev, ct);
        original.MarkAsReversed(rev.Id);
        await _db.SaveChangesAsync(ct);
        return rev;
    }
}

public record DeleteAccountSettlementCommand(int SettlementId) : IRequest<Result<bool>>;

/// <summary>
/// حذف نهائي لتسوية مُلغاة مسبقاً: يُحذف قيود العكس أولاً ثم القيدين الأساسيين.
/// </summary>
public class DeleteAccountSettlementHandler : IRequestHandler<DeleteAccountSettlementCommand, Result<bool>>
{
    private readonly IAccountingDbContext _db;
    private readonly IVoucherAttachmentDeletionService _attachmentDeletion;

    public DeleteAccountSettlementHandler(
        IAccountingDbContext db,
        IVoucherAttachmentDeletionService attachmentDeletion)
    {
        _db = db;
        _attachmentDeletion = attachmentDeletion;
    }

    public async Task<Result<bool>> Handle(DeleteAccountSettlementCommand req, CancellationToken ct)
    {
        try
        {
            var settlement = await _db.AccountSettlements.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == req.SettlementId, ct);
            if (settlement == null) return Result.Failure<bool>("التسوية غير موجودة");
            if (!settlement.IsDeleted)
                return Result.Failure<bool>(
                    "لا يمكن حذف تسوية نشطة. ألغِ التسوية أولاً (عكس القيود) ثم احذفها.");

            var reversalIds = new[]
            {
                settlement.SourceReversalJournalEntryId,
                settlement.TargetReversalJournalEntryId,
            }.Where(id => id is > 0).Select(id => id!.Value).ToList();

            var baseIds = new[] { settlement.SourceJournalEntryId, settlement.TargetJournalEntryId };
            var allIds = reversalIds.Concat(baseIds).Distinct().ToList();

            var entries = await _db.JournalEntries.IgnoreQueryFilters()
                .Include(e => e.Lines)
                .Where(e => allIds.Contains(e.Id))
                .ToListAsync(ct);

            var ceilingCheck = await CashBoxGuard.ValidateAfterExcludingEntriesAsync(_db, allIds, ct);
            if (ceilingCheck != null)
                return Result.Failure<bool>(ceilingCheck);

            await using var tx = await _db.BeginTransactionAsync(ct);

            // 1) قيود العكس أولاً
            foreach (var id in reversalIds)
            {
                await _attachmentDeletion.DeleteAllForJournalEntryAsync(id, ct);
                SoftDeleteEntry(entries, id);
            }

            // 2) القيود الأساسية
            foreach (var id in baseIds)
            {
                await _attachmentDeletion.DeleteAllForJournalEntryAsync(id, ct);
                SoftDeleteEntry(entries, id);
            }

            _db.AccountSettlements.Remove(settlement);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Result.Success(true);
        }
        catch (DomainException ex) { return Result.Failure<bool>(ex.Message); }
        catch (DbUpdateException ex)
        {
            return Result.Failure<bool>(
                $"تعذّر حذف التسوية — تحقق من ارتباطات القيود أو سقوف الحسابات. ({ex.InnerException?.Message ?? ex.Message})");
        }
    }

    private static void SoftDeleteEntry(List<JournalEntry> entries, int id)
    {
        var entry = entries.FirstOrDefault(e => e.Id == id);
        if (entry == null || entry.IsDeleted) return;
        entry.MarkAsDeleted();
        foreach (var line in entry.Lines)
            line.MarkAsDeleted();
    }
}

public record GetAccountSettlementTransitMovementsQuery(
    DateTime? From, DateTime? To, string? Currency, int? TransitAccountId)
    : IRequest<List<SettlementTransitMovementDto>>;

public class GetAccountSettlementTransitMovementsHandler
    : IRequestHandler<GetAccountSettlementTransitMovementsQuery, List<SettlementTransitMovementDto>>
{
    private readonly IAccountingDbContext _db;
    public GetAccountSettlementTransitMovementsHandler(IAccountingDbContext db) => _db = db;

    public async Task<List<SettlementTransitMovementDto>> Handle(
        GetAccountSettlementTransitMovementsQuery req, CancellationToken ct)
    {
        var settings = await _db.AccountSettlementSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == AccountSettlementSettings.SingletonId, ct);
        var transitIds = GetAccountSettlementSettingsHandler.MapSettings(settings).TransitAccounts.Values
            .Distinct().ToList();
        if (transitIds.Count == 0) return new List<SettlementTransitMovementDto>();

        if (req.TransitAccountId.HasValue)
            transitIds = transitIds.Where(id => id == req.TransitAccountId.Value).ToList();

        var settlements = await _db.AccountSettlements.IgnoreQueryFilters().AsNoTracking()
            .Where(s => transitIds.Contains(s.SourceTransitAccountId) || transitIds.Contains(s.TargetTransitAccountId))
            .ToListAsync(ct);
        var settlementMap = settlements.ToDictionary(s => s.SettlementNumber, s => s);

        var refNumbers = settlements.Select(s => s.SettlementNumber).Distinct().ToList();
        if (refNumbers.Count == 0) return new List<SettlementTransitMovementDto>();

        var entries = await _db.JournalEntries.AsNoTracking()
            .Where(e => e.ReferenceNumber != null && refNumbers.Contains(e.ReferenceNumber))
            .Where(e => e.ReferenceType == "AccountSettlement" || e.ReferenceType == "AccountSettlementReversal")
            .Where(e => e.Status == JournalEntryStatus.Posted || e.Status == JournalEntryStatus.Reversed)
            .ToListAsync(ct);

        if (req.From.HasValue)
            entries = entries.Where(e => e.EntryDate >= req.From.Value.Date).ToList();
        if (req.To.HasValue)
            entries = entries.Where(e => e.EntryDate <= req.To.Value.Date.AddDays(1).AddTicks(-1)).ToList();

        var entryIds = entries.Select(e => e.Id).ToList();
        var lines = await _db.JournalEntryLines.AsNoTracking()
            .Where(l => entryIds.Contains(l.JournalEntryId) && transitIds.Contains(l.AccountId))
            .ToListAsync(ct);

        var accMap = await _db.Accounts.AsNoTracking()
            .Where(a => transitIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => (a.Code, a.NameAr), ct);

        var result = new List<SettlementTransitMovementDto>();
        foreach (var line in lines)
        {
            var entry = entries.First(e => e.Id == line.JournalEntryId);
            if (req.Currency != null && !entry.Currency.Equals(req.Currency, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!entry.ReferenceNumber!.StartsWith("STL-")) continue;
            if (!settlementMap.TryGetValue(entry.ReferenceNumber, out var stl)) continue;

            var isReversal = entry.ReferenceType == "AccountSettlementReversal";
            var side = line.AccountId == stl.SourceTransitAccountId ? "Source" : "Target";
            var isDebit = isReversal ? !line.IsDebit : line.IsDebit;

            accMap.TryGetValue(line.AccountId, out var acc);
            result.Add(new SettlementTransitMovementDto(
                stl.Id, stl.SettlementNumber, entry.EntryDate,
                line.AccountId, acc.Code, acc.NameAr, entry.Currency,
                isDebit, line.Amount, side,
                stl.IsDeleted || isReversal,
                entry.Id, entry.EntryNumber));
        }

        return result
            .OrderByDescending(x => x.SettlementDate)
            .ThenByDescending(x => x.SettlementId)
            .Take(1000)
            .ToList();
    }
}

internal static class SettlementEngine
{
    internal sealed record BuildResult(
        string SourceCurrency,
        string TargetCurrency,
        decimal TargetAmount,
        decimal ExchangeRate,
        decimal FxGainLossAmount,
        decimal FxDiscountAmount,
        int? FxGainLossAccountId,
        int? FxDiscountAccountId,
        int SourceTransitAccountId,
        int TargetTransitAccountId,
        List<(int AccountId, bool IsDebit, decimal Amount, string? LineDesc)> SourceLines,
        List<(int AccountId, bool IsDebit, decimal Amount, string? LineDesc)> TargetLines,
        SettlementCreatePreviewDto PreviewDto);

    public static decimal EffectiveFx(decimal fxGainLoss, decimal fxDiscount)
    {
        if (fxGainLoss == 0 || fxDiscount == 0) return fxGainLoss;
        var d = Math.Abs(fxDiscount);
        if (fxGainLoss > 0) return Math.Max(0, fxGainLoss - d);
        return Math.Min(0, fxGainLoss + d);
    }

    public static void EnsureLinesBalanced(IReadOnlyList<(int AccountId, bool IsDebit, decimal Amount, string? LineDesc)> lines)
    {
        decimal dr = 0, cr = 0;
        foreach (var l in lines)
        {
            if (l.IsDebit) dr += l.Amount;
            else cr += l.Amount;
        }
        if (Math.Round(dr, 3) != Math.Round(cr, 3))
            throw new DomainException($"القيد غير متوازن: مدين={dr:N3} دائن={cr:N3}");
    }

    private static List<(int AccountId, bool IsDebit, decimal Amount, string? LineDesc)> BuildTargetLines(
        int targetAccountId,
        int tgtTransit,
        decimal targetAmount,
        decimal effectiveFx,
        int? fxAccountId,
        decimal fxDiscount,
        int? fxDiscountAccountId,
        decimal fxGainLoss)
    {
        var lines = new List<(int, bool, decimal, string?)>
        {
            (targetAccountId, true, targetAmount, "إيداع هدف"),
        };

        var transitNet = targetAmount - effectiveFx;
        if (transitNet != 0)
        {
            // موجب → دائن وسيط (يُموّل الهدف وفرق العملة) | سالب → مدين وسيط
            lines.Add((
                tgtTransit,
                transitNet < 0,
                Math.Abs(transitNet),
                "وسيط تسوية"));
        }

        if (effectiveFx > 0 && fxAccountId.HasValue)
            lines.Add((fxAccountId.Value, false, effectiveFx, "ربح فرق عملة"));
        else if (effectiveFx < 0 && fxAccountId.HasValue)
            lines.Add((fxAccountId.Value, true, Math.Abs(effectiveFx), "خسارة فرق عملة"));

        if (fxDiscount > 0 && fxDiscountAccountId.HasValue)
        {
            if (fxGainLoss > 0)
                lines.Add((fxDiscountAccountId.Value, true, fxDiscount, "خصم فرق صرف"));
            else
                lines.Add((fxDiscountAccountId.Value, false, fxDiscount, "خصم فرق صرف"));
        }

        EnsureLinesBalanced(lines);
        return lines;
    }

    public static async Task<BuildResult> BuildAsync(
        IAccountingDbContext db,
        IMediator mediator,
        PreviewAccountSettlementQuery req,
        bool previewOnly,
        CancellationToken ct,
        int? sourceTransitOverride = null,
        int? targetTransitOverride = null)
    {
        var srcCur = req.SourceCurrency.Trim().ToUpperInvariant();
        var tgtCur = req.TargetCurrency.Trim().ToUpperInvariant();
        if (req.SourceAmount <= 0) throw new DomainException("مبلغ المصدر يجب أن يكون موجباً");

        var settings = await db.AccountSettlementSettings.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == AccountSettlementSettings.SingletonId, ct);
        var settingsDto = GetAccountSettlementSettingsHandler.MapSettings(settings);

        var srcTransit = sourceTransitOverride
            ?? (settingsDto.TransitAccounts.TryGetValue(srcCur, out var st) ? st : 0);
        var tgtTransit = targetTransitOverride
            ?? (settingsDto.TransitAccounts.TryGetValue(tgtCur, out var tt) ? tt : 0);
        if (srcTransit <= 0 || tgtTransit <= 0)
            throw new DomainException("حدّد حسابات الوسيط للعملتين في إعدادات التسوية");

        var toDate = req.SettlementDate.Date;
        var from = new DateTime(toDate.Year, 1, 1);
        var srcBalances = await mediator.Send(new GetAccountBalancesQuery(
            from, toDate, req.SourceAccountId, null, false, null, true, false, true), ct);
        var srcBal = srcBalances.Rows
            .Where(r => r.AccountId == req.SourceAccountId && r.Currency.Equals(srcCur, StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.DebitBalance - r.CreditBalance);

        var tgtBalances = await mediator.Send(new GetAccountBalancesQuery(
            from, toDate, req.TargetAccountId, null, false, null, true, false, true), ct);
        var tgtBal = tgtBalances.Rows
            .Where(r => r.AccountId == req.TargetAccountId && r.Currency.Equals(tgtCur, StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.DebitBalance - r.CreditBalance);

        var (baseCur, bulletinName, bulletinAt, rates) = await LoadBulletinAsync(db, req.SettlementDate, ct);
        var bulletinCross = CrossRate(srcCur, tgtCur, baseCur, rates);
        var exchangeRate = req.ExchangeRate ?? (srcCur == tgtCur ? 1m : bulletinCross);
        if (exchangeRate <= 0) throw new DomainException("سعر الصرف غير صالح");

        var targetAmount = req.TargetAmount ?? Math.Round(req.SourceAmount * exchangeRate, 3);
        if (targetAmount <= 0) throw new DomainException("مبلغ الهدف غير صالح");

        var bulletinTarget = Math.Round(req.SourceAmount * bulletinCross, 3);
        var fxGainLoss = srcCur == tgtCur ? 0m : Math.Round(targetAmount - bulletinTarget, 3);
        var fxDiscount = Math.Abs(req.FxDiscountAmount ?? 0m);

        if (srcCur == tgtCur && fxDiscount > 0)
            throw new DomainException("لا ينطبق خصم فرق الصرف عند تسوية نفس العملة");
        if (fxDiscount > 0)
        {
            if (fxGainLoss == 0)
                throw new DomainException("لا يوجد فرق صرف لتطبيق الخصم عليه");
            if (fxDiscount > Math.Abs(fxGainLoss) + 0.001m)
                throw new DomainException("الخصم أكبر من فرق الصرف");
            if (!settingsDto.FxDiscountAccountId.HasValue)
                throw new DomainException("حدّد حساب خصم فرق الصرف في إعدادات التسوية");
        }

        var effectiveFx = EffectiveFx(fxGainLoss, fxDiscount);

        int? fxAccountId = null;
        if (effectiveFx > 0) fxAccountId = settingsDto.FxGainAccountId;
        else if (effectiveFx < 0) fxAccountId = settingsDto.FxLossAccountId;
        if (effectiveFx != 0 && !fxAccountId.HasValue)
            throw new DomainException("حدّد حساب أرباح/خسائر العملة في إعدادات التسوية");

        if (!previewOnly && srcBal < req.SourceAmount - 0.001m)
            throw new DomainException($"رصيد المصدر ({srcBal:N3} {srcCur}) أقل من مبلغ التسوية");

        var accIds = new[] {
            req.SourceAccountId, req.TargetAccountId, srcTransit, tgtTransit,
            fxAccountId ?? 0, settingsDto.FxDiscountAccountId ?? 0,
        }.Where(id => id > 0).Distinct().ToList();
        var accMap = await db.Accounts.AsNoTracking()
            .Where(a => accIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => (a.Code, a.NameAr), ct);

        var srcLines = new List<(int, bool, decimal, string?)>
        {
            (srcTransit, true, req.SourceAmount, "وسيط تسوية"),
            (req.SourceAccountId, false, req.SourceAmount, "تصفير مصدر"),
        };
        EnsureLinesBalanced(srcLines);

        var tgtLines = BuildTargetLines(
            req.TargetAccountId, tgtTransit, targetAmount,
            effectiveFx, fxAccountId, fxDiscount, settingsDto.FxDiscountAccountId, fxGainLoss);

        static List<SettlementJournalLinePreviewDto> MapLines(
            IEnumerable<(int Id, bool Dr, decimal Amt, string? D)> lines,
            string ccy,
            Dictionary<int, (string Code, string NameAr)> map) =>
            lines.Select(l =>
            {
                map.TryGetValue(l.Id, out var a);
                return new SettlementJournalLinePreviewDto(l.Id, a.Code, a.NameAr, l.Dr, l.Amt, ccy, l.D);
            }).ToList();

        var preview = new SettlementCreatePreviewDto(
            new SettlementPreviewDto(srcBal, tgtBal, bulletinCross, targetAmount, fxGainLoss,
                fxDiscount, effectiveFx, baseCur, bulletinName, bulletinAt,
                SettlementRateDisplay.FormatFromCrossRate(exchangeRate),
                SettlementRateDisplay.FormatForCurrencyPair(srcCur, tgtCur, baseCur, rates)),
            MapLines(srcLines.Select(x => (x.Item1, x.Item2, x.Item3, x.Item4)), srcCur, accMap),
            MapLines(tgtLines.Select(x => (x.Item1, x.Item2, x.Item3, x.Item4)), tgtCur, accMap));

        return new BuildResult(srcCur, tgtCur, targetAmount, exchangeRate, fxGainLoss, fxDiscount,
            fxAccountId, fxDiscount > 0 ? settingsDto.FxDiscountAccountId : null,
            srcTransit, tgtTransit, srcLines, tgtLines, preview);
    }

    private static async Task<(string Base, string? Name, DateTime? At,
        Dictionary<string, (decimal Rate, int Operation)> Rates)> LoadBulletinAsync(
        IAccountingDbContext db, DateTime date, CancellationToken ct)
    {
        var at = date.Date.AddDays(1).AddTicks(-1);
        var bulletin = await db.CurrencyRateBulletins.Include(b => b.Lines)
            .Where(b => b.Status == CurrencyRateBulletinStatus.Published && b.EffectiveAt <= at)
            .OrderByDescending(b => b.EffectiveAt).ThenByDescending(b => b.Id)
            .FirstOrDefaultAsync(ct);
        var rates = new Dictionary<string, (decimal, int)>(StringComparer.OrdinalIgnoreCase);
        var baseCur = "IQD";
        if (bulletin == null) return (baseCur, null, null, rates);
        baseCur = (bulletin.BaseCurrency ?? "IQD").Trim().ToUpperInvariant();
        foreach (var line in bulletin.Lines.Where(l => l.Rate > 0))
            rates[line.Currency.Trim().ToUpperInvariant()] = (line.Rate, (int)line.Operation);
        return (baseCur, bulletin.Name, bulletin.EffectiveAt, rates);
    }

    private static decimal CrossRate(string from, string to, string baseCur,
        IReadOnlyDictionary<string, (decimal Rate, int Operation)> rates)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 1m;
        var fromMult = ToBaseMultiplier(from, baseCur, rates);
        var toMult = ToBaseMultiplier(to, baseCur, rates);
        if (toMult == 0) return 0;
        return fromMult / toMult;
    }

    private static decimal ToBaseMultiplier(string ccy, string baseCur,
        IReadOnlyDictionary<string, (decimal Rate, int Operation)> rates)
    {
        var c = ccy.Trim().ToUpperInvariant();
        var b = baseCur.Trim().ToUpperInvariant();
        if (c == b) return 1m;
        if (!rates.TryGetValue(c, out var e) || e.Rate <= 0) return 1m;
        return e.Operation == 2 ? 1m / e.Rate : e.Rate;
    }
}
