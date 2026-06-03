using IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Exceptions;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.PostDraftJournalEntries;

public class PostDraftJournalEntriesHandler
    : IRequestHandler<PostDraftJournalEntriesCommand, Result<PostDraftJournalEntriesResultDto>>
{
    private const int MaxBatchSize = 500;
    private const string JournalPostPermission = "Accounting.JournalEntries.Post";

    private readonly IAccountingDbContext _db;
    private readonly IPeriodResolver _periods;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditLogger _audit;

    public PostDraftJournalEntriesHandler(
        IAccountingDbContext db,
        IPeriodResolver periods,
        ICurrentUserService currentUser,
        IAuditLogger audit)
    {
        _db = db;
        _periods = periods;
        _currentUser = currentUser;
        _audit = audit;
    }

    public async Task<Result<PostDraftJournalEntriesResultDto>> Handle(
        PostDraftJournalEntriesCommand req,
        CancellationToken ct)
    {
        var issues = new List<PostDraftJournalEntryIssueDto>();
        var posted = 0;
        var skipped = 0;
        var failed = 0;

        if (req.AllowedCashBoxIds is not null && req.AllowedCashBoxIds.Count == 0)
        {
            return Result.Success(new PostDraftJournalEntriesResultDto(0, 0, 0, issues));
        }

        var q = _db.JournalEntries
            .Include(e => e.Lines)
            .Include(e => e.VoucherType)
            .Where(e => e.Status == JournalEntryStatus.Draft);

        q = ApplyListFilters(q, req);

        if (req.AllowedCashBoxIds is not null)
        {
            var allowedIds = req.AllowedCashBoxIds.ToList();
            var cbAccountIds = await CashBoxPartySource.GetAccountIdsByPartyIdsAsync(_db, allowedIds, ct);
            q = q.Where(e => e.Lines.Any(l => cbAccountIds.Contains(l.AccountId)));
        }

        var entries = await q
            .OrderBy(e => e.EntryDate)
            .ThenBy(e => e.Id)
            .Take(MaxBatchSize + 1)
            .ToListAsync(ct);

        if (entries.Count > MaxBatchSize)
        {
            return Result.Failure<PostDraftJournalEntriesResultDto>(
                $"عدد القيود غير المرحَّلة ({entries.Count}) يتجاوز الحد الأقصى ({MaxBatchSize}). ضيِّق الفلاتر وحاول مجدداً.");
        }

        if (entries.Count == 0)
        {
            return Result.Success(new PostDraftJournalEntriesResultDto(0, 0, 0, issues));
        }

        var activeFy = await _db.FiscalYears.AsNoTracking()
            .FirstOrDefaultAsync(f => f.IsActive, ct);

        foreach (var entry in entries)
        {
            var label = FormatEntryLabel(entry);
            try
            {
                if (!CanPostEntry(entry))
                {
                    skipped++;
                    issues.Add(new PostDraftJournalEntryIssueDto(
                        entry.Id, entry.EntryNumber, label, "لا تملك صلاحية ترحيل هذا القيد", "Skipped"));
                    continue;
                }

                if (entry.ReferenceType is "CashBoxTransfer" or "CashBoxTransferReversal")
                {
                    skipped++;
                    issues.Add(new PostDraftJournalEntryIssueDto(
                        entry.Id, entry.EntryNumber, label,
                        "قيد مناقلة صناديق — يُرحَّل من نافذة المناقلات", "Skipped"));
                    continue;
                }

                if (activeFy != null)
                {
                    var d = entry.EntryDate.Date;
                    if (d < activeFy.StartDate.Date || d > activeFy.EndDate.Date)
                    {
                        failed++;
                        issues.Add(new PostDraftJournalEntryIssueDto(
                            entry.Id, entry.EntryNumber, label,
                            $"تاريخ القيد ({d:yyyy-MM-dd}) خارج السنة المالية النشطة '{activeFy.Name}'", "Failed"));
                        continue;
                    }
                }

                try
                {
                    await _periods.ResolveAsync(entry.EntryDate, ct);
                }
                catch (ClosedPeriodException ex)
                {
                    failed++;
                    issues.Add(new PostDraftJournalEntryIssueDto(
                        entry.Id, entry.EntryNumber, label, ex.Message, "Failed"));
                    continue;
                }

                var lineSnapshots = entry.Lines
                    .Select(l => new CashBoxGuard.LineSnapshot(l.AccountId, l.IsDebit, l.Amount))
                    .ToList();
                var accountIds = lineSnapshots.Select(l => l.AccountId).Distinct().ToList();

                var currencyCheck = await EnsureCurrencyHasActiveBulletin(entry.Currency, entry.EntryDate, ct);
                if (currencyCheck != null)
                {
                    failed++;
                    issues.Add(new PostDraftJournalEntryIssueDto(
                        entry.Id, entry.EntryNumber, label, currencyCheck, "Failed"));
                    continue;
                }

                var cashBoxCheck = await CashBoxGuard.ValidateAsync(
                    _db, lineSnapshots, entry.Currency, entry.VoucherTypeId,
                    excludeJournalEntryId: null, ct);
                if (cashBoxCheck != null)
                {
                    failed++;
                    issues.Add(new PostDraftJournalEntryIssueDto(
                        entry.Id, entry.EntryNumber, label, cashBoxCheck, "Failed"));
                    continue;
                }

                var partyCheck = await FinancialPartyGuard.ValidateAsync(
                    _db, accountIds, entry.Currency, ct);
                if (partyCheck != null)
                {
                    failed++;
                    issues.Add(new PostDraftJournalEntryIssueDto(
                        entry.Id, entry.EntryNumber, label, partyCheck, "Failed"));
                    continue;
                }

                entry.Post(_currentUser.UserId?.ToString() ?? "system");
                posted++;

                var auditEntityType = entry.VoucherTypeId.HasValue ? "Voucher" : "JournalEntry";
                await _audit.LogAsync(
                    entityType: auditEntityType,
                    entityId: entry.Id.ToString(),
                    action: AuditActions.Post,
                    summary: entry.VoucherTypeId.HasValue && entry.VoucherSequence.HasValue
                        ? $"ترحيل سند {entry.VoucherType?.Code}-{entry.VoucherSequence}"
                        : $"ترحيل قيد رقم {entry.EntryNumber}",
                    details: new
                    {
                        entry.EntryNumber,
                        entry.VoucherTypeId,
                        entry.VoucherSequence,
                        entry.TotalDebit,
                        entry.TotalCredit,
                        entry.Currency,
                        status = entry.Status.ToString(),
                    },
                    ct: ct);
            }
            catch (UnbalancedJournalEntryException ex)
            {
                failed++;
                issues.Add(new PostDraftJournalEntryIssueDto(
                    entry.Id, entry.EntryNumber, label, ex.Message, "Failed"));
            }
            catch (DomainException ex)
            {
                failed++;
                issues.Add(new PostDraftJournalEntryIssueDto(
                    entry.Id, entry.EntryNumber, label, ex.Message, "Failed"));
            }
        }

        if (posted > 0)
            await _db.SaveChangesAsync(ct);

        return Result.Success(new PostDraftJournalEntriesResultDto(posted, skipped, failed, issues));
    }

    private bool CanPostEntry(JournalEntry entry)
    {
        if (_currentUser.IsSuperAdmin) return true;
        if (entry.VoucherType?.Code is { Length: > 0 } code)
            return _currentUser.HasPermission($"Accounting.Vouchers.{code.ToUpperInvariant()}.Post");
        return _currentUser.HasPermission(JournalPostPermission);
    }

    private static string? FormatEntryLabel(JournalEntry entry) =>
        entry.VoucherSequence.HasValue && entry.VoucherType != null
            ? $"{entry.VoucherType.Code}-{entry.VoucherSequence.Value}"
            : null;

    private static IQueryable<JournalEntry> ApplyListFilters(
        IQueryable<JournalEntry> q,
        PostDraftJournalEntriesCommand req)
    {
        if (!string.IsNullOrWhiteSpace(req.SearchTerm))
        {
            var t = req.SearchTerm.Trim();
            string? voucherCode = null;
            int? voucherSeq = null;
            var dashIdx = t.IndexOf('-');
            if (dashIdx > 0 && dashIdx < t.Length - 1
                && int.TryParse(t[(dashIdx + 1)..], out var seq))
            {
                voucherCode = t[..dashIdx];
                voucherSeq = seq;
            }
            q = q.Where(e =>
                e.EntryNumber.Contains(t)
                || e.Description.Contains(t)
                || (e.ManualNumber != null && e.ManualNumber.Contains(t))
                || (voucherCode != null && voucherSeq != null
                    && e.VoucherType != null
                    && e.VoucherType.Code == voucherCode
                    && e.VoucherSequence == voucherSeq));
        }

        if (req.FromDate.HasValue)
        {
            var fromDay = req.FromDate.Value.Date;
            q = q.Where(e => e.EntryDate >= fromDay);
        }
        if (req.ToDate.HasValue)
        {
            var toDayEnd = req.ToDate.Value.Date.AddDays(1).AddTicks(-1);
            q = q.Where(e => e.EntryDate <= toDayEnd);
        }
        if (req.VoucherTypeId.HasValue)
            q = q.Where(e => e.VoucherTypeId == req.VoucherTypeId.Value);

        if (req.ExcludeSidebarVoucherTypes && !req.VoucherTypeId.HasValue)
        {
            q = q.Where(e => e.VoucherTypeId == null
                          || (e.VoucherType != null && !e.VoucherType.ShowInSidebar));
        }

        return q;
    }

    private async Task<string?> EnsureCurrencyHasActiveBulletin(
        string currency, DateTime entryDate, CancellationToken ct)
    {
        var cur = (currency ?? "IQD").Trim().ToUpperInvariant();
        var atUtc = (entryDate.Kind == DateTimeKind.Utc ? entryDate : entryDate.ToUniversalTime())
            .Date.AddDays(1).AddTicks(-1);

        var bulletin = await _db.CurrencyRateBulletins
            .Include(b => b.Lines)
            .Where(b => b.Status == CurrencyRateBulletinStatus.Published && b.EffectiveAt <= atUtc)
            .OrderByDescending(b => b.EffectiveAt).ThenByDescending(b => b.Id)
            .FirstOrDefaultAsync(ct);

        if (bulletin != null && string.Equals(bulletin.BaseCurrency, cur, StringComparison.OrdinalIgnoreCase))
            return null;

        if (bulletin == null)
        {
            if (cur == "IQD") return null;
            return $"العملة {cur} غير مُسعَّرة — لا توجد نشرة أسعار منشورة سارية بتاريخ {entryDate:yyyy-MM-dd}.";
        }

        var hasLine = bulletin.Lines.Any(l =>
            string.Equals(l.Currency, cur, StringComparison.OrdinalIgnoreCase));
        if (!hasLine)
            return $"العملة {cur} غير مُسعَّرة في نشرة الأسعار '{bulletin.Name}'.";

        return null;
    }
}
