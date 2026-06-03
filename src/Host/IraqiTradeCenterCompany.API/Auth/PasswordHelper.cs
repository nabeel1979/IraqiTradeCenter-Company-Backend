using System.Security.Cryptography;
using System.Text;

namespace IraqiTradeCenterCompany.API.Auth;

public static class PasswordHelper
{
    private const int Iterations = 100_000;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string hash)
    {
        var parts = hash.Split(':');
        if (parts.Length != 2) return false;
        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), salt,
            Iterations, HashAlgorithmName.SHA256, KeySize);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private const string PasswordChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789!@#$%";

    /// <summary>كلمة مرور عشوائية آمنة (بدون أحرف مُلتبسة مثل 0/O/I/l/1).</summary>
    public static string Generate(int length = 12)
    {
        if (length < 8) length = 8;
        var bytes = RandomNumberGenerator.GetBytes(length);
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = PasswordChars[bytes[i] % PasswordChars.Length];
        return new string(chars);
    }

    public static bool IsStrongEnough(string password, int minLength = 8)
        => !string.IsNullOrWhiteSpace(password) && password.Length >= minLength;
}
