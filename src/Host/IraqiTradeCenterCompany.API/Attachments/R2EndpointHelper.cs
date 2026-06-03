using System;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace IraqiTradeCenterCompany.API.Attachments;

/// <summary>
/// بناء endpoint Cloudflare R2 S3-API واختبار اتصال TLS قبل أي طلب S3.
/// </summary>
internal static class R2EndpointHelper
{
    public const string DefaultJurisdiction = "default";
    public const string EuJurisdiction = "eu";

    /// <summary>
    /// يُرجع عنوان S3 API الكامل (https://…) من Account ID والاختصاص.
    /// </summary>
    public static string BuildServiceUrl(string accountId, string? jurisdiction = null)
    {
        var id = (accountId ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("R2 Account ID is required.", nameof(accountId));

        var host = BuildHost(id, jurisdiction);
        return $"https://{host}";
    }

    public static string BuildHost(string accountId, string? jurisdiction = null)
    {
        var id = (accountId ?? string.Empty).Trim();
        var j = NormalizeJurisdiction(jurisdiction);
        return j == EuJurisdiction
            ? $"{id}.eu.r2.cloudflarestorage.com"
            : $"{id}.r2.cloudflarestorage.com";
    }

    public static string NormalizeJurisdiction(string? jurisdiction)
    {
        var j = (jurisdiction ?? DefaultJurisdiction).Trim().ToLowerInvariant();
        return j is "eu" or "eu-jurisdiction" or "fedramp" ? EuJurisdiction : DefaultJurisdiction;
    }

    /// <summary>
    /// يتحقّق أن Account ID بصيغة hex بطول 32 (كما في لوحة Cloudflare).
    /// </summary>
    public static bool IsValidAccountIdFormat(string? accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId) || accountId.Length != 32) return false;
        return accountId.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }

    public static async Task<(bool Ok, string Detail)> ProbeTlsAsync(string host, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host))
            return (false, "empty host");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout);

        TcpClient? tcp = null;
        try
        {
            tcp = new TcpClient();
            await tcp.ConnectAsync(host, 443, linked.Token);

            using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
            var opts = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12,
            };
            await ssl.AuthenticateAsClientAsync(opts, linked.Token);
            return (true, $"TLS OK ({ssl.NegotiatedCipherSuite})");
        }
        catch (Exception ex)
        {
            var inner = ex.InnerException?.Message;
            var msg = inner != null ? $"{ex.Message} // {inner}" : ex.Message;
            return (false, msg);
        }
        finally
        {
            tcp?.Dispose();
        }
    }

    public static async Task<(bool Ok, string Detail)> ProbeDnsAsync(string host, CancellationToken ct)
    {
        try
        {
            var addrs = await Dns.GetHostAddressesAsync(host);
            if (addrs.Length == 0) return (false, "no addresses");
            var ips = string.Join(", ", addrs.Take(3).Select(a => a.ToString()));
            return (true, ips);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
