using IraqiTradeCenterCompany.API.Auth;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.API.Settings;

public interface IEmailSettingsService
{
    Task<EmailSettings> GetAsync(CancellationToken ct = default);
    Task<EmailSettings> UpdateAsync(Action<EmailSettings> mutate, string? updatedBy, CancellationToken ct = default);
    void Invalidate();
}

public class EmailSettingsService : IEmailSettingsService
{
    private readonly AuthDbContext _db;
    private readonly ILogger<EmailSettingsService> _log;
    private static EmailSettings? _cached;
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public EmailSettingsService(AuthDbContext db, ILogger<EmailSettingsService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<EmailSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cached != null) return _cached;
        await _gate.WaitAsync(ct);
        try
        {
            if (_cached != null) return _cached;
            var row = await _db.EmailSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1, ct);
            if (row == null)
            {
                row = new EmailSettings { Id = 1 };
                _db.EmailSettings.Add(row);
                try { await _db.SaveChangesAsync(ct); }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Seeding default EmailSettings failed; using in-memory defaults.");
                    row = new EmailSettings { Id = 1 };
                }
            }
            _cached = row;
            return row;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EmailSettings> UpdateAsync(Action<EmailSettings> mutate, string? updatedBy, CancellationToken ct = default)
    {
        var row = await _db.EmailSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (row == null)
        {
            row = new EmailSettings { Id = 1 };
            _db.EmailSettings.Add(row);
        }
        mutate(row);
        row.UpdatedAtUtc = DateTime.UtcNow;
        row.UpdatedBy = updatedBy;
        await _db.SaveChangesAsync(ct);
        _cached = row;
        return row;
    }

    public void Invalidate() => _cached = null;
}
