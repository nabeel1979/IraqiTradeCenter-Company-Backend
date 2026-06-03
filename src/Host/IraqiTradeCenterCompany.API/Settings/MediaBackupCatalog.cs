using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IraqiTradeCenterCompany.API.Settings;

public sealed class DatabaseBackupFileDto
{
    public string YearFolder { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// فهرسة ملفات .bak تحت {MediaRoot}/{Year}/database/ مع حماية من path traversal.
/// </summary>
public static class MediaBackupCatalog
{
    public static string? ResolveRoot(string? mediaRootPath)
    {
        if (string.IsNullOrWhiteSpace(mediaRootPath)) return null;
        try { return Path.GetFullPath(mediaRootPath.Trim()); }
        catch { return null; }
    }

    public static IReadOnlyList<DatabaseBackupFileDto> ListDatabaseBackups(string? mediaRootPath)
    {
        var root = ResolveRoot(mediaRootPath);
        if (root == null || !Directory.Exists(root)) return Array.Empty<DatabaseBackupFileDto>();

        var list = new List<DatabaseBackupFileDto>();
        foreach (var yearDir in Directory.EnumerateDirectories(root))
        {
            var yearFolder = Path.GetFileName(yearDir);
            if (!IsSafeSegment(yearFolder)) continue;

            var dbDir = Path.Combine(yearDir, "database");
            if (!Directory.Exists(dbDir)) continue;

            foreach (var file in Directory.EnumerateFiles(dbDir, "*.bak"))
            {
                var fileName = Path.GetFileName(file);
                if (!IsSafeFileName(fileName)) continue;
                var info = new FileInfo(file);
                list.Add(new DatabaseBackupFileDto
                {
                    YearFolder = yearFolder,
                    FileName = fileName,
                    SizeBytes = info.Length,
                    CreatedAtUtc = info.LastWriteTimeUtc,
                });
            }
        }

        return list.OrderByDescending(x => x.CreatedAtUtc).ToList();
    }

    public static string? ResolveDatabaseBackupPath(string? mediaRootPath, string yearFolder, string fileName)
    {
        var root = ResolveRoot(mediaRootPath);
        if (root == null) return null;
        if (!IsSafeSegment(yearFolder) || !IsSafeFileName(fileName)) return null;
        if (!fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return null;

        var candidate = Path.GetFullPath(Path.Combine(root, yearFolder, "database", fileName));
        var dbDir = Path.GetFullPath(Path.Combine(root, yearFolder, "database"));
        if (!candidate.StartsWith(dbDir, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(candidate) ? candidate : null;
    }

    private static bool IsSafeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        if (segment is "." or "..") return false;
        return segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !segment.Contains('/') && !segment.Contains('\\');
    }

    private static bool IsSafeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return false;
        if (fileName is "." or "..") return false;
        return fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !fileName.Contains('/') && !fileName.Contains('\\');
    }
}
