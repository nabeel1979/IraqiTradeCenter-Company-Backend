using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace IraqiTradeCenterCompany.API.Auth;

/// <summary>
/// يضمن وجود أعمدة/تعديلات auth الحرجة حتى لو فشلت أو تأخرت migrations EF.
/// </summary>
public static class AuthSchemaBootstrapper
{
    public static async Task EnsureAsync(IConfiguration cfg, CancellationToken ct = default)
    {
        var connStr = cfg.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connStr)) return;

        await using var cn = new SqlConnection(connStr);
        await cn.OpenAsync(ct);

        await ExecAsync(cn, @"
IF COL_LENGTH('auth.Users', 'MustChangePassword') IS NULL
BEGIN
    ALTER TABLE auth.Users
        ADD MustChangePassword bit NOT NULL
            CONSTRAINT DF_auth_Users_MustChangePassword DEFAULT(0);
END

IF COL_LENGTH('auth.Users', 'AvatarBase64') IS NULL
BEGIN
    ALTER TABLE auth.Users ADD AvatarBase64 nvarchar(max) NULL;
END", ct);
    }

    private static async Task ExecAsync(SqlConnection cn, string sql, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
