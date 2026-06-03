using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IraqiTradeCenterCompany.API.Attachments;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IraqiTradeCenterCompany.API.Settings;

public class MediaBackupRunRequest
{
    public int FiscalYearId { get; set; }
}

public class MediaBackupModuleResult
{
    public string Code { get; set; } = default!;
    public int EntryCount { get; set; }
    public int AttachmentCount { get; set; }
    public long SizeBytes { get; set; }
    public string? DataFile { get; set; }
}

public class MediaBackupRunResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string YearFolder { get; set; } = default!;
    public string RootPath { get; set; } = default!;
    public long TotalSizeBytes { get; set; }
    public string? DatabaseFile { get; set; }
    public bool DatabaseSyncedToR2 { get; set; }
    public string? DatabaseR2Key { get; set; }
    public int LocalDatabaseBackupsPurged { get; set; }
    public int R2DatabaseBackupsPurged { get; set; }
    public List<MediaBackupModuleResult> Modules { get; set; } = new();
    public string? ManifestFile { get; set; }
}

public interface IMediaBackupRunner
{
    Task<MediaBackupRunResult> RunAsync(MediaBackupRunRequest request, string? runBy, CancellationToken ct = default);
    bool IsRunning { get; }
}

/// <summary>
/// ينشئ أرشيف الميديا لسنة مالية:
///   {Root}/{Year}/database/*.bak
///   {Root}/{Year}/{RV|PV|JV|JE}/data/*.json
///   {Root}/{Year}/{Code}/attachments/...
/// </summary>
public class MediaBackupRunner : IMediaBackupRunner
{
    private static readonly SemaphoreSlim _runGate = new(1, 1);
    private static readonly string[] ModuleCodes = { "RV", "PV", "JV", "JE" };

    private readonly IMediaBackupSettingsService _settings;
    private readonly IAccountingDbContext _db;
    private readonly IAttachmentStorageRegistry _storage;
    private readonly IConfiguration _config;
    private readonly ILogger<MediaBackupRunner> _log;

    public MediaBackupRunner(
        IMediaBackupSettingsService settings,
        IAccountingDbContext db,
        IAttachmentStorageRegistry storage,
        IConfiguration config,
        ILogger<MediaBackupRunner> log)
    {
        _settings = settings;
        _db = db;
        _storage = storage;
        _config = config;
        _log = log;
    }

    public bool IsRunning => _runGate.CurrentCount == 0;

    public async Task<MediaBackupRunResult> RunAsync(MediaBackupRunRequest request, string? runBy, CancellationToken ct = default)
    {
        if (!await _runGate.WaitAsync(0, ct))
            throw new InvalidOperationException("عملية أرشفة أخرى قيد التشغيل — انتظر اكتمالها.");

        try
        {
            var settings = await _settings.GetAsync(ct);
            if (string.IsNullOrWhiteSpace(settings.MediaRootPath))
                throw new InvalidOperationException("حدّد مسار أرشيف الميديا أولاً.");

            var (pathOk, pathMsg) = MediaBackupSettingsService.TestPath(settings.MediaRootPath);
            if (!pathOk) throw new InvalidOperationException(pathMsg);

            var fy = await _db.FiscalYears.AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == request.FiscalYearId, ct)
                ?? throw new InvalidOperationException("السنة المالية غير موجودة.");

            await _settings.UpdateAsync(row =>
            {
                row.LastRunStatus = "Running";
                row.LastRunError = null;
            }, runBy, ct);

            var yearFolder = SanitizeFolderName(fy.Name);
            var root = Path.GetFullPath(settings.MediaRootPath!.Trim());
            var yearRoot = Path.Combine(root, yearFolder);
            Directory.CreateDirectory(yearRoot);

            var result = new MediaBackupRunResult
            {
                YearFolder = yearFolder,
                RootPath = yearRoot,
            };

            if (settings.IncludeDatabaseBackup)
            {
                result.DatabaseFile = await BackupDatabaseAsync(yearRoot, yearFolder, ct);
                if (result.DatabaseFile != null)
                {
                    result.TotalSizeBytes += new FileInfo(result.DatabaseFile).Length;

                    if (settings.SyncDatabaseBackupToR2)
                    {
                        var r2 = _storage.GetByName("R2") as R2AttachmentStorage
                            ?? throw new InvalidOperationException("R2 storage is not available.");
                        var (synced, r2Key) = await SyncDatabaseBackupToR2Async(r2, result.DatabaseFile, yearFolder, ct);
                        result.DatabaseSyncedToR2 = synced;
                        result.DatabaseR2Key = r2Key;
                        if (synced)
                        {
                            result.R2DatabaseBackupsPurged = await ApplyR2DatabaseRetentionAsync(
                                r2,
                                yearFolder,
                                settings.R2DatabaseBackupKeepCount,
                                ct);
                        }
                    }

                    result.LocalDatabaseBackupsPurged = ApplyLocalDatabaseRetention(
                        Path.Combine(yearRoot, "database"),
                        settings.ServerDatabaseBackupKeepCount);
                }
            }

            if (settings.IncludeVoucherData || settings.IncludeAttachments)
            {
                var entries = await _db.JournalEntries.AsNoTracking()
                    .Where(e => e.FiscalYearId == fy.Id)
                    .Include(e => e.VoucherType)
                    .Include(e => e.Lines)
                    .OrderBy(e => e.Id)
                    .ToListAsync(ct);

                var entryIds = entries.Select(e => e.Id).ToList();
                var attachments = settings.IncludeAttachments
                    ? await _db.VoucherAttachments.AsNoTracking()
                        .Where(a => entryIds.Contains(a.JournalEntryId) && !a.IsDeleted)
                        .ToListAsync(ct)
                    : new();

                var entryMap = entries.ToDictionary(e => e.Id);

                foreach (var code in ModuleCodes)
                {
                    var moduleEntries = entries.Where(e => ResolveModuleCode(e) == code).ToList();
                    if (moduleEntries.Count == 0 && !attachments.Any(a =>
                        entryMap.TryGetValue(a.JournalEntryId, out var en) && ResolveModuleCode(en) == code))
                        continue;

                    var moduleDir = Path.Combine(yearRoot, code);
                    var dataDir = Path.Combine(moduleDir, "data");
                    var attDir = Path.Combine(moduleDir, "attachments");
                    Directory.CreateDirectory(dataDir);
                    if (settings.IncludeAttachments) Directory.CreateDirectory(attDir);

                    var modResult = new MediaBackupModuleResult { Code = code, EntryCount = moduleEntries.Count };

                    if (settings.IncludeVoucherData && moduleEntries.Count > 0)
                    {
                        var export = moduleEntries.Select(e => new
                        {
                            e.Id,
                            e.EntryNumber,
                            e.EntryDate,
                            VoucherRef = FormatVoucherRef(e),
                            VoucherTypeCode = e.VoucherType?.Code,
                            e.VoucherSequence,
                            e.ManualNumber,
                            e.Status,
                            e.Currency,
                            e.Description,
                            e.TotalDebit,
                            e.TotalCredit,
                            e.PostedAt,
                            e.PostedBy,
                            Lines = e.Lines.Select(l => new
                            {
                                l.Id,
                                l.AccountId,
                                l.IsDebit,
                                l.Amount,
                                l.Description,
                            }).ToList(),
                        }).ToList();

                        var jsonPath = Path.Combine(dataDir, $"{code}_{yearFolder}.json");
                        await File.WriteAllTextAsync(jsonPath,
                            JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }),
                            ct);
                        modResult.DataFile = Path.GetRelativePath(yearRoot, jsonPath).Replace('\\', '/');
                        modResult.SizeBytes += new FileInfo(jsonPath).Length;
                    }

                    if (settings.IncludeAttachments)
                    {
                        var local = _storage.GetByName("Local");
                        var r2 = _storage.GetByName("R2");
                        foreach (var att in attachments.Where(a =>
                            entryMap.TryGetValue(a.JournalEntryId, out var en) && ResolveModuleCode(en) == code))
                        {
                            if (!entryMap.TryGetValue(att.JournalEntryId, out var entry)) continue;
                            var refName = SanitizeFolderName(FormatVoucherRef(entry));
                            var destFolder = Path.Combine(attDir, $"{refName}_{entry.Id}");
                            Directory.CreateDirectory(destFolder);
                            var destFile = Path.Combine(destFolder, SanitizeFileName(att.OriginalFileName));

                            try
                            {
                                await using var stream = await OpenAttachmentStreamAsync(local, r2, att.IsOnLocal, att.IsOnR2, att.StorageKey, ct);
                                await using var fs = File.Create(destFile);
                                await stream.CopyToAsync(fs, ct);
                                modResult.AttachmentCount++;
                                modResult.SizeBytes += new FileInfo(destFile).Length;
                            }
                            catch (Exception ex)
                            {
                                _log.LogWarning(ex, "Skipping attachment {Id} during media backup", att.Id);
                            }
                        }
                    }

                    result.Modules.Add(modResult);
                    result.TotalSizeBytes += modResult.SizeBytes;
                }
            }

            var manifest = new
            {
                fiscalYear = fy.Name,
                fiscalYearId = fy.Id,
                createdAtUtc = DateTime.UtcNow.ToString("o"),
                createdBy = runBy,
                databaseFile = result.DatabaseFile != null
                    ? Path.GetRelativePath(yearRoot, result.DatabaseFile).Replace('\\', '/')
                    : null,
                modules = result.Modules,
                totalSizeBytes = result.TotalSizeBytes,
            };
            var manifestPath = Path.Combine(yearRoot, "manifest.json");
            await File.WriteAllTextAsync(manifestPath,
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), ct);
            result.ManifestFile = "manifest.json";
            result.TotalSizeBytes += new FileInfo(manifestPath).Length;

            result.Success = true;
            result.Message = "تم إنشاء أرشيف الميديا بنجاح.";

            await _settings.UpdateAsync(row =>
            {
                row.LastRunStatus = "Success";
                row.LastRunAtUtc = DateTime.UtcNow;
                row.LastRunError = null;
                row.LastRunYearFolder = yearFolder;
            }, runBy, ct);

            return result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Media backup failed");
            await _settings.UpdateAsync(row =>
            {
                row.LastRunStatus = "Failed";
                row.LastRunAtUtc = DateTime.UtcNow;
                row.LastRunError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            }, runBy, ct);
            throw;
        }
        finally
        {
            _runGate.Release();
        }
    }

    private async Task<string?> BackupDatabaseAsync(string yearRoot, string yearFolder, CancellationToken ct)
    {
        var cs = _config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(cs)) return null;

        var builder = new SqlConnectionStringBuilder(cs);
        var dbName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(dbName)) return null;

        var dbDir = Path.Combine(yearRoot, "database");
        Directory.CreateDirectory(dbDir);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{dbName}_{yearFolder}_{stamp}.bak";
        var fullPath = Path.Combine(dbDir, fileName);

        await using var cn = new SqlConnection(cs);
        await cn.OpenAsync(ct);
        var sql = $@"
BACKUP DATABASE [{dbName}]
TO DISK = @path
WITH COMPRESSION, CHECKSUM, INIT, STATS = 10;";
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 600 };
        cmd.Parameters.AddWithValue("@path", fullPath);
        await cmd.ExecuteNonQueryAsync(ct);
        return fullPath;
    }

    private async Task<(bool synced, string? r2Key)> SyncDatabaseBackupToR2Async(
        R2AttachmentStorage r2,
        string localBakPath,
        string yearFolder,
        CancellationToken ct)
    {
        var fileName = Path.GetFileName(localBakPath);
        var key = $"media-backup/{SanitizeR2Segment(yearFolder)}/database/{SanitizeR2Segment(fileName)}";
        var size = new FileInfo(localBakPath).Length;

        await using var fs = File.OpenRead(localBakPath);
        await r2.UploadWithKeyAsync(key, fs, "application/octet-stream", ct);
        _log.LogInformation("Database backup synced to R2: {Key} ({Size} bytes)", key, size);
        return (true, key);
    }

    private static Task<int> ApplyR2DatabaseRetentionAsync(
        R2AttachmentStorage r2,
        string yearFolder,
        int keepCount,
        CancellationToken ct)
        => MediaBackupR2Catalog.ApplyRetentionAsync(r2, keepCount, yearFolder, ct);

    /// <summary>يُبقي أحدث <paramref name="keepCount"/> نسخ .bak ويحذف الأقدم.</summary>
    private static int ApplyLocalDatabaseRetention(string dbDir, int keepCount)
    {
        if (!Directory.Exists(dbDir) || keepCount <= 0) return 0;

        var files = Directory.GetFiles(dbDir, "*.bak")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        var purged = 0;
        foreach (var old in files.Skip(keepCount))
        {
            try
            {
                old.Delete();
                purged++;
            }
            catch { /* best effort */ }
        }
        return purged;
    }

    private static string SanitizeR2Segment(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\' }).ToHashSet();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }

    private static async Task<Stream> OpenAttachmentStreamAsync(
        IAttachmentStorage local,
        IAttachmentStorage r2,
        bool isOnLocal,
        bool isOnR2,
        string storageKey,
        CancellationToken ct)
    {
        if (isOnLocal)
        {
            try { return await local.OpenReadAsync(storageKey, ct); }
            catch (FileNotFoundException) when (isOnR2) { /* fallback */ }
        }
        if (isOnR2) return await r2.OpenReadAsync(storageKey, ct);
        throw new FileNotFoundException($"Attachment not found: {storageKey}");
    }

    private static string ResolveModuleCode(
        IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities.JournalEntry e)
    {
        var code = e.VoucherType?.Code?.Trim().ToUpperInvariant();
        if (code is "RV" or "PV" or "JV") return code;
        return "JE";
    }

    private static string FormatVoucherRef(
        IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities.JournalEntry e)
    {
        if (e.VoucherType != null && e.VoucherSequence.HasValue)
            return $"{e.VoucherType.Code}-{e.VoucherSequence}";
        return e.EntryNumber;
    }

    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "unknown";
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var arr = (name ?? "file").Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(arr).Trim();
        return string.IsNullOrWhiteSpace(s) ? "file" : s;
    }
}
