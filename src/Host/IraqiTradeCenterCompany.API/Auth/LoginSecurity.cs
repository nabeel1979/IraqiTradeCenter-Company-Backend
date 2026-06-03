using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace IraqiTradeCenterCompany.API.Auth;

/// <summary>
/// إعدادات حماية تسجيل الدخول، تُقرأ من قسم <c>Security:Login</c> في appsettings.
/// </summary>
public class LoginSecurityOptions
{
    public const string SectionName = "Security:Login";

    /// <summary>حد محاولات الدخول لكل عنوان IP (Rate Limiting على مستوى الشبكة).</summary>
    public IpRateLimitOptions IpRateLimit { get; set; } = new();

    /// <summary>قفل الحساب المؤقت بعد عدد من المحاولات الفاشلة المتتالية.</summary>
    public LockoutOptions Lockout { get; set; } = new();
}

public class IpRateLimitOptions
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}

public class LockoutOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockMinutes { get; set; } = 15;
    public int FailureWindowMinutes { get; set; } = 15;
}

/// <summary>
/// قفل حساب مؤقت ضد هجمات تخمين كلمة المرور. يتتبّع المحاولات الفاشلة لكل
/// مُعرِّف (اسم مستخدم/هاتف) ضمن نافذة زمنية، ويقفل الدخول لمدة محدّدة عند
/// تجاوز الحد. الحالة تُحفَظ في الذاكرة (<see cref="IMemoryCache"/>).
/// </summary>
public interface ILoginThrottle
{
    /// <summary>هل المُعرِّف مقفول حالياً؟ يُخرج عدد الثواني المتبقية للفتح.</summary>
    bool IsLocked(string key, out int retryAfterSeconds);

    /// <summary>تسجيل محاولة فاشلة. قد تؤدي إلى القفل عند بلوغ الحد.</summary>
    void RecordFailure(string key);

    /// <summary>تصفير العدّاد بعد نجاح الدخول.</summary>
    void Reset(string key);
}

public class LoginThrottle : ILoginThrottle
{
    private readonly IMemoryCache _cache;
    private readonly LoginSecurityOptions _options;

    public LoginThrottle(IMemoryCache cache, IOptions<LoginSecurityOptions> options)
    {
        _cache = cache;
        _options = options.Value;
    }

    private sealed class Counter
    {
        public int Failures;
        public DateTime FirstFailureUtc;
        public DateTime? LockedUntilUtc;
    }

    private static string CacheKey(string key) => $"login-throttle:{key.Trim().ToLowerInvariant()}";

    public bool IsLocked(string key, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        if (string.IsNullOrWhiteSpace(key)) return false;

        if (_cache.TryGetValue(CacheKey(key), out Counter? c) && c?.LockedUntilUtc is { } until)
        {
            var remaining = until - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                retryAfterSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
                return true;
            }
        }
        return false;
    }

    public void RecordFailure(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        var lockout = _options.Lockout;
        var max = lockout.MaxFailedAttempts > 0 ? lockout.MaxFailedAttempts : 5;
        var lockMinutes = lockout.LockMinutes > 0 ? lockout.LockMinutes : 15;
        var windowMinutes = lockout.FailureWindowMinutes > 0 ? lockout.FailureWindowMinutes : 15;

        var ck = CacheKey(key);
        var now = DateTime.UtcNow;
        var c = _cache.TryGetValue(ck, out Counter? existing) && existing != null
            ? existing
            : new Counter { FirstFailureUtc = now };

        // نافذة الفشل انتهت بلا قفل → نبدأ عدّاً جديداً.
        if (c.LockedUntilUtc == null && now - c.FirstFailureUtc > TimeSpan.FromMinutes(windowMinutes))
        {
            c.Failures = 0;
            c.FirstFailureUtc = now;
        }

        c.Failures++;
        if (c.Failures >= max)
            c.LockedUntilUtc = now.AddMinutes(lockMinutes);

        var ttl = TimeSpan.FromMinutes(Math.Max(lockMinutes, windowMinutes) + 1);
        _cache.Set(ck, c, ttl);
    }

    public void Reset(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _cache.Remove(CacheKey(key));
    }
}
