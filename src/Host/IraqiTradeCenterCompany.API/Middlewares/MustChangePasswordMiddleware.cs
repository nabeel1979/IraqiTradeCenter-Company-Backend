using System.Text.Json;

namespace IraqiTradeCenterCompany.API.Middlewares;

/// <summary>
/// عند وجود claim <c>mustChangePassword</c> في JWT يُحجب كل الـ API ما عدا
/// تغيير كلمة المرور وجلب بيانات المستخدم الحالي.
/// </summary>
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.User.Identity?.IsAuthenticated != true)
        {
            await _next(ctx);
            return;
        }

        var mustChange = ctx.User.FindFirst("mustChangePassword")?.Value == "true";
        if (!mustChange)
        {
            await _next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? "";
        if (IsAllowlisted(path, ctx.Request.Method))
        {
            await _next(ctx);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            success = false,
            errors = new[] { "يجب تغيير كلمة المرور قبل متابعة استخدام النظام" },
            code = "MUST_CHANGE_PASSWORD",
        }));
    }

    private static bool IsAllowlisted(string path, string method)
    {
        if (path.Equals("/api/auth/change-password", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsPost(method))
            return true;

        if (path.Equals("/api/users/me", StringComparison.OrdinalIgnoreCase)
            && HttpMethods.IsGet(method))
            return true;

        return false;
    }
}
