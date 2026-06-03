using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IraqiTradeCenterCompany.API.Attachments;

namespace IraqiTradeCenterCompany.API.Settings;

public sealed class R2DatabaseBackupFileDto
{
    public string R2Key { get; set; } = default!;
    public string YearFolder { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public long SizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// فهرسة وإدارة نسخ .bak على Cloudflare R2 تحت media-backup/{year}/database/.
/// </summary>
public static class MediaBackupR2Catalog
{
    public const string RootPrefix = "media-backup/";

    public static async Task<IReadOnlyList<R2DatabaseBackupFileDto>> ListDatabaseBackupsAsync(
        R2AttachmentStorage r2,
        CancellationToken ct = default)
    {
        var objects = await r2.ListObjectsWithPrefixAsync(RootPrefix, ct);
        var list = new List<R2DatabaseBackupFileDto>();
        foreach (var obj in objects)
        {
            if (!TryParseDatabaseBackupKey(obj.Key, out var yearFolder, out var fileName))
                continue;
            list.Add(new R2DatabaseBackupFileDto
            {
                R2Key = obj.Key,
                YearFolder = yearFolder,
                FileName = fileName,
                SizeBytes = obj.SizeBytes,
                CreatedAtUtc = obj.LastModifiedUtc,
            });
        }
        return list.OrderByDescending(x => x.CreatedAtUtc).ToList();
    }

    /// <summary>يُبقي أحدث <paramref name="keepCount"/> نسخ .bak لكل سنة مالية على R2.</summary>
    public static async Task<int> ApplyRetentionAsync(
        R2AttachmentStorage r2,
        int keepCount,
        string? yearFolder = null,
        CancellationToken ct = default)
    {
        if (keepCount <= 0) return 0;

        var all = await ListDatabaseBackupsAsync(r2, ct);
        var grouped = all
            .Where(f => yearFolder == null || string.Equals(f.YearFolder, yearFolder, StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => f.YearFolder, StringComparer.OrdinalIgnoreCase);

        var purged = 0;
        foreach (var group in grouped)
        {
            foreach (var old in group.OrderByDescending(f => f.CreatedAtUtc).Skip(keepCount))
            {
                await r2.DeleteAsync(old.R2Key, ct);
                purged++;
            }
        }
        return purged;
    }

    public static bool TryParseDatabaseBackupKey(string key, out string yearFolder, out string fileName)
    {
        yearFolder = string.Empty;
        fileName = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (!key.StartsWith(RootPrefix, StringComparison.OrdinalIgnoreCase)) return false;

        var parts = key.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // media-backup / {year} / database / {file.bak}
        if (parts.Length != 4) return false;
        if (!parts[0].Equals("media-backup", StringComparison.OrdinalIgnoreCase)) return false;
        if (!parts[2].Equals("database", StringComparison.OrdinalIgnoreCase)) return false;
        if (!parts[3].EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return false;

        yearFolder = parts[1];
        fileName = parts[3];
        return !string.IsNullOrWhiteSpace(yearFolder) && !string.IsNullOrWhiteSpace(fileName);
    }
}
