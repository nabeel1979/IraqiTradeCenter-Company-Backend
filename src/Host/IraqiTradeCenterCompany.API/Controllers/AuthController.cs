using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IraqiTradeCenterCompany.API.Auth;
using IraqiTradeCenterCompany.API.Auth.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace IraqiTradeCenterCompany.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthDbContext _db;
    private readonly IConfiguration _config;
    private readonly IPermissionService _permissions;
    private readonly ILoginThrottle _throttle;

    public AuthController(AuthDbContext db, IConfiguration config, IPermissionService permissions,
        ILoginThrottle throttle)
    {
        _db = db;
        _config = config;
        _permissions = permissions;
        _throttle = throttle;
    }

    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Phone) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { success = false, errors = new[] { "اسم المستخدم وكلمة المرور مطلوبان" } });

        var identifier = req.Phone.Trim();
        var identifierLower = identifier.ToLowerInvariant();

        // قفل الحساب المؤقت بعد محاولات فاشلة متكررة.
        if (_throttle.IsLocked(identifier, out var retryAfter))
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                success = false,
                errors = new[] { $"تم قفل الدخول مؤقتاً بسبب محاولات فاشلة متكررة. حاول بعد {retryAfter} ثانية." }
            });

        var user = await _db.Users.FirstOrDefaultAsync(u =>
            u.IsActive
            && (u.Phone.ToLower() == identifierLower || u.FullName.ToLower() == identifierLower));
        if (user is null)
        {
            _throttle.RecordFailure(identifier);
            return Unauthorized(new { success = false, errors = new[] { "اسم المستخدم غير موجود أو الحساب غير فعّال" } });
        }

        if (!PasswordHelper.Verify(req.Password, user.PasswordHash))
        {
            _throttle.RecordFailure(identifier);
            return Unauthorized(new { success = false, errors = new[] { "كلمة المرور غير صحيحة — راجع الأحرف الكبيرة والصغيرة والرموز" } });
        }

        _throttle.Reset(identifier);
        var payload = await BuildAuthPayloadAsync(user);
        return Ok(new { success = true, data = payload });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
            return BadRequest(new { success = false, errors = new[] { "كلمة المرور الحالية والجديدة مطلوبتان" } });

        if (!PasswordHelper.IsStrongEnough(req.NewPassword))
            return BadRequest(new { success = false, errors = new[] { "كلمة المرور الجديدة يجب أن تكون 8 أحرف على الأقل" } });

        if (req.CurrentPassword == req.NewPassword)
            return BadRequest(new { success = false, errors = new[] { "كلمة المرور الجديدة يجب أن تختلف عن الحالية" } });

        var uid = GetUserId();
        if (uid == Guid.Empty) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == uid);
        if (user is null) return Unauthorized();

        if (!PasswordHelper.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new { success = false, errors = new[] { "كلمة المرور الحالية غير صحيحة" } });

        user.PasswordHash = PasswordHelper.Hash(req.NewPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();

        var payload = await BuildAuthPayloadAsync(user);
        return Ok(new { success = true, data = payload });
    }

    private async Task<object> BuildAuthPayloadAsync(CompanyUser user)
    {
        var roleCodes = await _db.UserRoles
            .Where(ur => ur.UserId == user.Id)
            .Join(_db.Roles.Where(r => r.IsActive), ur => ur.RoleId, r => r.Id, (ur, r) => r.Code)
            .ToListAsync();
        if (roleCodes.Count == 0) roleCodes.Add(user.Role);

        var perms = await _permissions.GetUserPermissionsAsync(user.Id);
        var isSuper = await _permissions.IsSuperAdminAsync(user.Id);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new("phone", user.Phone),
            new("companyId", "1"),
        };
        foreach (var rc in roleCodes.Distinct())
            claims.Add(new Claim(ClaimTypes.Role, rc));
        if (isSuper && !roleCodes.Contains("SuperAdmin"))
            claims.Add(new Claim(ClaimTypes.Role, "SuperAdmin"));

        if (user.MustChangePassword)
            claims.Add(new Claim("mustChangePassword", "true"));
        else if (!isSuper)
        {
            foreach (var p in perms)
                claims.Add(new Claim("perm", p));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddHours(_config.GetValue("Jwt:ExpirationHours", 24));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt = expiry.ToString("O"),
            user = new
            {
                id = user.Id,
                fullName = user.FullName,
                phone = user.Phone,
                role = user.Role,
                roles = roleCodes,
                permissions = perms.ToArray(),
                isSuperAdmin = isSuper,
                mustChangePassword = user.MustChangePassword,
                avatarBase64 = user.AvatarBase64,
            }
        };
    }

    private Guid GetUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(idStr, out var g) ? g : Guid.Empty;
    }
}

public record LoginRequest(string Phone, string Password);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
