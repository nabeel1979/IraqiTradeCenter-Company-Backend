using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IraqiTradeCenterCompany.API.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IraqiTradeCenterCompany.API.Settings;

public interface IMediaBackupSettingsService
{
    Task<MediaBackupSettings> GetAsync(CancellationToken ct = default);
    Task<MediaBackupSettings> UpdateAsync(Action<MediaBackupSettings> mutate, string? updatedBy, CancellationToken ct = default);
    void Invalidate();
}

public class MediaBackupSettingsService : IMediaBackupSettingsService
{
    private readonly AuthDbContext _db;
    private readonly ILogger<MediaBackupSettingsService> _log;
    private static MediaBackupSettings? _cached;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public MediaBackupSettingsService(AuthDbContext db, ILogger<MediaBackupSettingsService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<MediaBackupSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cached != null) return _cached;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached != null) return _cached;
            var row = await _db.MediaBackupSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
            if (row == null)
            {
                row = new MediaBackupSettings { Id = 1 };
                _db.MediaBackupSettings.Add(row);
                try { await _db.SaveChangesAsync(ct); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Seeding MediaBackupSettings failed; using in-memory defaults.");
                }
            }
            _cached = row;
            return row;
        }
        finally { _gate.Release(); }
    }

    public async Task<MediaBackupSettings> UpdateAsync(Action<MediaBackupSettings> mutate, string? updatedBy, CancellationToken ct = default)
    {
        var row = await _db.MediaBackupSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (row == null)
        {
            row = new MediaBackupSettings { Id = 1 };
            _db.MediaBackupSettings.Add(row);
        }
        mutate(row);
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = updatedBy;
        await _db.SaveChangesAsync(ct);
        Invalidate();
        return await GetAsync(ct);
    }

    public void Invalidate() => _cached = null;

    /// <summary>يتحقق أن المسار قابل للكتابة من حساب الـ App Pool.</summary>
    public static (bool ok, string message) TestPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return (false, "المسار فارغ — أدخل مساراً كاملاً على الخادم.");

        var full = Path.GetFullPath(path.Trim());
        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, $".write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return (true, $"المسار متاح للكتابة: {full}");
        }
        catch (Exception ex)
        {
            return (false, $"تعذّر الكتابة على المسار: {ex.Message}");
        }
    }
}
