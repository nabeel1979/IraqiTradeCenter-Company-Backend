using IraqiTradeCenterCompany.API.Licensing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IraqiTradeCenterCompany.API.Integration;

/// <summary>يتحقق من مفتاح التكامل (AuthKey من الترخيص) في رأس الطلب — للنظام الرئيسي فقط.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IntegrationAuthAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var cfg = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var opts = cfg.GetSection(ParentIntegrationOptions.SectionName).Get<ParentIntegrationOptions>()
                   ?? new ParentIntegrationOptions();
        var headerName = opts.IntegrationHeaderName ?? "X-ITC-Integration-Key";

        if (!context.HttpContext.Request.Headers.TryGetValue(headerName, out var provided)
            || string.IsNullOrWhiteSpace(provided))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                success = false,
                errors = new[] { "مفتاح التكامل مفقود في رأس الطلب" }
            });
            return;
        }

        var license = context.HttpContext.RequestServices.GetRequiredService<ILicenseService>();
        var licCfg = await license.GetConfigAsync(context.HttpContext.RequestAborted);
        var expected = licCfg.AuthKey?.Trim();
        if (string.IsNullOrEmpty(expected)
            || !string.Equals(provided.ToString().Trim(), expected, StringComparison.Ordinal))
        {
            context.Result = new UnauthorizedObjectResult(new
            {
                success = false,
                errors = new[] { "مفتاح التكامل غير صالح" }
            });
            return;
        }

        await next();
    }
}
