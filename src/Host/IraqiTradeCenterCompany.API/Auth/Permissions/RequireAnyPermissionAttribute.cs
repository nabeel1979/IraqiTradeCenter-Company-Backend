using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IraqiTradeCenterCompany.API.Auth.Permissions;

/// <summary>
/// يمرّر الطلب إذا امتلك المستخدم <b>أي</b> صلاحية من القائمة (OR).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireAnyPermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string[] _permissions;

    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        _permissions = permissions ?? Array.Empty<string>();
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (_permissions.Length == 0) return;

        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        if (user.IsInRole("SuperAdmin")) return;

        foreach (var perm in _permissions)
        {
            if (user.HasClaim("perm", perm)) return;
        }

        var userIdStr = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? user.FindFirst("sub")?.Value;
        if (Guid.TryParse(userIdStr, out var userId))
        {
            var svc = context.HttpContext.RequestServices.GetService(typeof(IPermissionService)) as IPermissionService;
            if (svc != null)
            {
                foreach (var perm in _permissions)
                {
                    if (await svc.HasPermissionAsync(userId, perm)) return;
                }
            }
        }

        context.Result = new ObjectResult(new
        {
            success = false,
            errors = new[] { "ليس لديك صلاحية كافية لهذه العملية." },
            requiredAnyPermission = _permissions,
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
