using System.Text.RegularExpressions;
using IraqiTradeCenterCompany.SharedKernel.Contacts;

namespace IraqiTradeCenterCompany.API.ContactRegistry;

public static class ContactNormalizer
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string? Normalize(string kind, string? raw, out string? display, out string? error)
    {
        display = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw)) return null;

        display = raw.Trim();

        if (kind == ContactKinds.Email)
        {
            var email = display.ToLowerInvariant();
            if (!EmailRegex.IsMatch(email))
            {
                error = "صيغة البريد الإلكتروني غير صالحة";
                return null;
            }
            return email;
        }

        if (ContactKinds.IsPhoneLike(kind))
        {
            var digits = new string(display.Where(c => char.IsDigit(c)).ToArray());
            if (digits.Length < 7)
            {
                error = "رقم الهاتف قصير جداً";
                return null;
            }
            if (digits.StartsWith("964", StringComparison.Ordinal))
                return digits;
            if (digits.StartsWith('0'))
                return "964" + digits[1..];
            return digits;
        }

        error = "نوع جهة اتصال غير معروف";
        return null;
    }
}
