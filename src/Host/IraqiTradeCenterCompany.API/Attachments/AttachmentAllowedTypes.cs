using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace IraqiTradeCenterCompany.API.Attachments;

/// <summary>الصيغ المسموحة في أرشيف مرفقات السندات.</summary>
internal static class AttachmentAllowedTypes
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tif", ".tiff",
        ".doc", ".docx", ".xls", ".xlsx", ".txt", ".csv",
        ".zip", ".rar", ".7z",
    };

    public static bool IsAllowed(IFormFile file, out string? errorMessage)
    {
        errorMessage = null;
        var ext = Path.GetExtension(file.FileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
        {
            errorMessage =
                "صيغة الملف غير مدعومة. المسموح: PDF، صور، Word، Excel، ZIP، RAR، 7Z.";
            return false;
        }
        return true;
    }
}
