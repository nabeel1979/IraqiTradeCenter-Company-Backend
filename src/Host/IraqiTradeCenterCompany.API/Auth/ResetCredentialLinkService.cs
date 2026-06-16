namespace IraqiTradeCenterCompany.API.Auth;

public sealed record ResetCredentialLinks(
    string ViewUrl,
    string ViewUrlCopyUsername,
    string ViewUrlCopyPassword);

public interface IResetCredentialLinkService
{
    ResetCredentialLinks Create(string username, string password);
}

public class ResetCredentialLinkService : IResetCredentialLinkService
{
    private readonly IResetCredentialViewCache _cache;
    private readonly IConfiguration _config;

    public ResetCredentialLinkService(IResetCredentialViewCache cache, IConfiguration config)
    {
        _cache = cache;
        _config = config;
    }

    public ResetCredentialLinks Create(string username, string password)
    {
        var token = _cache.Store(username, password);
        var baseUrl = (_config["App:CompanyFrontendUrl"] ?? "https://iraqitradecenter_company.gcc.iq").TrimEnd('/');
        var viewUrl = $"{baseUrl}/reset-credentials/{token}";
        return new ResetCredentialLinks(
            viewUrl,
            $"{viewUrl}?copy=username",
            $"{viewUrl}?copy=password");
    }
}
