using IraqiTradeCenterCompany.API.Auth;
using IraqiTradeCenterCompany.SharedKernel.Contacts;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.API.ContactRegistry;

public class ContactRegistryService : IContactRegistry
{
    private readonly AuthDbContext _db;

    public ContactRegistryService(AuthDbContext db) => _db = db;

    public async Task<IReadOnlyList<ContactPointDto>> GetForOwnerAsync(
        string ownerType, string ownerId, CancellationToken ct = default)
    {
        return await _db.ContactPoints.AsNoTracking()
            .Where(c => c.OwnerType == ownerType && c.OwnerId == ownerId)
            .OrderBy(c => c.Kind)
            .Select(c => new ContactPointDto
            {
                Id = c.Id,
                Kind = c.Kind,
                DisplayValue = c.DisplayValue,
                OwnerType = c.OwnerType,
                OwnerId = c.OwnerId,
            })
            .ToListAsync(ct);
    }

    public async Task<(bool Available, string? Error)> CheckAvailabilityAsync(
        string kind, string? value, string ownerType, string ownerId, CancellationToken ct = default)
    {
        var norm = ContactNormalizer.Normalize(kind, value, out _, out var err);
        if (err is not null) return (false, err);
        if (norm is null) return (true, null);

        var taken = await IsTakenAsync(kind, norm, ownerType, ownerId, ct);
        if (taken)
            return (false, DuplicateMessage(kind, value));

        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SyncOwnerAsync(
        string ownerType,
        string ownerId,
        string? email,
        string? phone,
        string? mobile,
        CancellationToken ct = default)
    {
        var desired = new List<(string Kind, string? Raw)>
        {
            (ContactKinds.Email, email),
            (ContactKinds.Phone, phone),
            (ContactKinds.Mobile, mobile),
        };

        var parsed = new List<(string Kind, string Norm, string Display)>();
        foreach (var (kind, raw) in desired)
        {
            var norm = ContactNormalizer.Normalize(kind, raw, out var display, out var err);
            if (err is not null) return (false, err);
            if (norm is null) continue;
            if (parsed.Any(p => p.Norm == norm && (ContactKinds.IsPhoneLike(kind) ? ContactKinds.IsPhoneLike(p.Kind) : p.Kind == kind)))
                return (false, DuplicateMessage(kind, raw));
            parsed.Add((kind, norm, display!));
        }

        foreach (var (kind, norm, _) in parsed)
        {
            if (await IsTakenAsync(kind, norm, ownerType, ownerId, ct))
                return (false, DuplicateMessage(kind, norm));
        }

        var existing = await _db.ContactPoints
            .Where(c => c.OwnerType == ownerType && c.OwnerId == ownerId)
            .ToListAsync(ct);
        _db.ContactPoints.RemoveRange(existing);

        foreach (var (kind, norm, display) in parsed)
        {
            _db.ContactPoints.Add(new ContactPoint
            {
                Kind = kind,
                NormalizedValue = norm,
                DisplayValue = display,
                OwnerType = ownerType,
                OwnerId = ownerId,
                CreatedAtUtc = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);
        return (true, null);
    }

    private async Task<bool> IsTakenAsync(string kind, string normalized, string ownerType, string ownerId, CancellationToken ct)
    {
        var q = _db.ContactPoints.AsNoTracking()
            .Where(c => !(c.OwnerType == ownerType && c.OwnerId == ownerId));

        if (ContactKinds.IsPhoneLike(kind))
        {
            return await q.AnyAsync(c =>
                (c.Kind == ContactKinds.Phone || c.Kind == ContactKinds.Mobile) &&
                c.NormalizedValue == normalized, ct);
        }

        return await q.AnyAsync(c => c.Kind == kind && c.NormalizedValue == normalized, ct);
    }

    private static string DuplicateMessage(string kind, string? value) => kind switch
    {
        ContactKinds.Email => $"البريد الإلكتروني «{value}» مستخدَم لدى سجل آخر (مستخدم أو طرف مالي)",
        ContactKinds.Mobile => $"رقم الموبايل «{value}» مستخدَم لدى سجل آخر",
        _ => $"رقم الهاتف «{value}» مستخدَم لدى سجل آخر (مستخدم أو طرف مالي)",
    };
}
