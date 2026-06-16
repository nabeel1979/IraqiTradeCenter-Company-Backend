namespace IraqiTradeCenterCompany.API.Integration;

public class ParentIntegrationOptions
{
    public const string SectionName = "ParentPlatform";

    public string ApiBaseUrl { get; set; } = "https://api-parent.iraqitradecenter.gcc.iq";
    public string SiteUrl { get; set; } = "https://parent.iraqitradecenter.gcc.iq";
    public string? IntegrationHeaderName { get; set; } = "X-ITC-Integration-Key";
}
