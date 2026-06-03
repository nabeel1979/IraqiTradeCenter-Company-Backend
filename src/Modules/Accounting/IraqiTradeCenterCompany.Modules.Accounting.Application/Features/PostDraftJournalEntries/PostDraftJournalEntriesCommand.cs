using IraqiTradeCenterCompany.SharedKernel.Models;
using MediatR;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Features.PostDraftJournalEntries;

public record PostDraftJournalEntriesCommand(
    string? SearchTerm = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int? VoucherTypeId = null,
    bool ExcludeSidebarVoucherTypes = false,
    IReadOnlyCollection<int>? AllowedCashBoxIds = null
) : IRequest<Result<PostDraftJournalEntriesResultDto>>;

public record PostDraftJournalEntriesResultDto(
    int PostedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<PostDraftJournalEntryIssueDto> Issues
);

public record PostDraftJournalEntryIssueDto(
    int EntryId,
    string EntryNumber,
    string? VoucherNumber,
    string Reason,
    string Kind // "Skipped" | "Failed"
);
