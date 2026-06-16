using Microsoft.Extensions.Caching.Memory;

namespace IraqiTradeCenterCompany.API.Auth;

public sealed record ResetCredentialPayload(string Username, string Password);

public interface IResetCredentialViewCache
{
    string Store(string username, string password, TimeSpan? ttl = null);
    ResetCredentialPayload? TryGet(string token);
}

public class ResetCredentialViewCache : IResetCredentialViewCache
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public ResetCredentialViewCache(IMemoryCache cache) => _cache = cache;

    public string Store(string username, string password, TimeSpan? ttl = null)
    {
        var token = Guid.NewGuid().ToString("N");
        _cache.Set(
            Key(token),
            new ResetCredentialPayload(username, password),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl });
        return token;
    }

    public ResetCredentialPayload? TryGet(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != 32) return null;
        return _cache.TryGetValue(Key(token), out ResetCredentialPayload? p) ? p : null;
    }

    private static string Key(string token) => $"reset-cred:{token}";
}
