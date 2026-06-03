using System;
using System.IO;
using System.Linq;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;

namespace IraqiTradeCenterCompany.API.Attachments;

/// <summary>
/// يبني مسار المرفقات بنفس منطق أرشيف الميديا:
/// <c>{Root}/{Year}/{RV|PV|JV|JE}/attachments/{VoucherRef}_{EntryId}/file</c>
/// </summary>
public static class AttachmentStoragePathHelper
{
    public static string ResolveModuleCode(JournalEntry entry)
    {
        var code = entry.VoucherType?.Code?.Trim().ToUpperInvariant();
        if (code is "RV" or "PV" or "JV") return code;
        return "JE";
    }

    public static string FormatVoucherRef(JournalEntry entry)
    {
        if (entry.VoucherType != null && entry.VoucherSequence.HasValue)
            return $"{entry.VoucherType.Code}-{entry.VoucherSequence}";
        return entry.EntryNumber;
    }

    public static string BuildLogicalFolder(string fiscalYearName, JournalEntry entry)
    {
        var yearFolder = SanitizeFolderSegment(fiscalYearName);
        var module = ResolveModuleCode(entry);
        var refName = SanitizeFolderSegment(FormatVoucherRef(entry));
        return $"{yearFolder}/{module}/attachments/{refName}_{entry.Id}";
    }

    public static string SanitizeFolderSegment(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).ToHashSet();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }
}
