using IraqiTradeCenterCompany.API.Auth;
using IraqiTradeCenterCompany.API.Auth.Permissions;
using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.SharedKernel.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.API.Controllers;

[ApiController]
[Authorize]
[Route("api/contact-points")]
public class ContactPointsController : ControllerBase
{
    private readonly AuthDbContext _auth;
    private readonly IAccountingDbContext _acc;
    private readonly IContactRegistry _registry;

    public ContactPointsController(AuthDbContext auth, IAccountingDbContext acc, IContactRegistry registry)
    {
        _auth = auth;
        _acc = acc;
        _registry = registry;
    }

    [HttpGet]
    [RequirePermission(PermissionRegistry.System.CompanySettings.Read)]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] string? kind,
        [FromQuery] string? ownerType,
        CancellationToken ct)
    {
        var q = _auth.ContactPoints.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind))
            q = q.Where(c => c.Kind == kind.Trim());
        if (!string.IsNullOrWhiteSpace(ownerType))
            q = q.Where(c => c.OwnerType == ownerType.Trim());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(c => c.DisplayValue.Contains(s) || c.NormalizedValue.Contains(s));
        }

        var rows = await q.OrderByDescending(c => c.CreatedAtUtc).Take(500).ToListAsync(ct);

        var userIds = rows.Where(r => r.OwnerType == "User")
            .Select(r => Guid.TryParse(r.OwnerId, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).Distinct().ToList();

        var userNames = await _auth.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(x => x.Id.ToString(), x => x.FullName, ct);

        var partyIds = rows.Where(r => r.OwnerType == "FinancialParty")
            .Select(r => int.TryParse(r.OwnerId, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();

        var partyNames = await (
            from p in _acc.FinancialParties.AsNoTracking()
            join a in _acc.Accounts.AsNoTracking() on p.AccountId equals a.Id
            where partyIds.Contains(p.Id)
            select new { p.Id, a.NameAr }
        ).ToDictionaryAsync(x => x.Id.ToString(), x => x.NameAr, ct);

        var data = rows.Select(r => new
        {
            r.Id,
            r.Kind,
            r.DisplayValue,
            r.NormalizedValue,
            r.OwnerType,
            r.OwnerId,
            ownerLabel = r.OwnerType switch
            {
                "User" => userNames.GetValueOrDefault(r.OwnerId),
                "FinancialParty" => partyNames.GetValueOrDefault(r.OwnerId),
                _ => null,
            },
        });

        return Ok(new { success = true, data });
    }

    [HttpGet("check")]
    [RequirePermission(PermissionRegistry.System.CompanySettings.Read)]
    public async Task<IActionResult> Check(
        [FromQuery] string kind,
        [FromQuery] string value,
        [FromQuery] string ownerType,
        [FromQuery] string ownerId,
        CancellationToken ct)
    {
        var (available, error) = await _registry.CheckAvailabilityAsync(kind, value, ownerType, ownerId, ct);
        return Ok(new { success = true, data = new { available, error } });
    }
}
