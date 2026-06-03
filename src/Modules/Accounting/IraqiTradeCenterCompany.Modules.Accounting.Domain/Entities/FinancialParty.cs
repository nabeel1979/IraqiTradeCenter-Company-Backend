using System.Text.Json;
using IraqiTradeCenterCompany.SharedKernel.Common;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;

namespace IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;

/// <summary>
/// طرف مالي فردي (مورد / عميل / مصرف) تحت نوع معين.
/// الاسم يُخزَّن حصراً في الحساب المرتبط (<see cref="Account"/>) لتحقيق
/// مزامنة كاملة بين شجرة الحسابات وبطاقة الطرف.
/// </summary>
public class FinancialParty : BaseEntity
{
    public int CategoryId          { get; private set; }
    public int AccountId           { get; private set; }

    /// <summary>
    /// سقف الائتمان لكل عملة (مدين/دائن) — مخزَّن كـ JSON بالشكل
    /// {"USD": {"debit": 1000, "credit": 500}, "IQD": {"debit": 0, "credit": 0}}
    /// </summary>
    public string? CreditLimits      { get; private set; }

    /// <summary>قائمة رموز العملات المسموحة — مخزَّنة كـ JSON array (مثلاً ["IQD","USD"])</summary>
    public string? AllowedCurrencies { get; private set; }

    /// <summary>
    /// IBAN لكل عملة — مخزَّن كـ JSON بالشكل {"IQD": "IQ98...", "USD": "US33..."}
    /// يُستخدم لأطراف نوع المصرف فقط.
    /// </summary>
    public string? CurrencyIbans { get; private set; }

    // معلومات الاتصال
    public string? Phone           { get; private set; }
    public string? Mobile          { get; private set; }
    public string? Email           { get; private set; }
    public string? Address         { get; private set; }
    public string? AddressEn       { get; private set; }
    public string? ContactPerson   { get; private set; }
    public string? Notes           { get; private set; }

    /// <summary>رقم الحساب المصرفي (IBAN/رقم حساب) — يُستخدم لأطراف نوع المصرف.</summary>
    public string? BankAccountNumber { get; private set; }

    /// <summary>رمز السويفت (SWIFT/BIC) — يُستخدم لأطراف نوع المصرف.</summary>
    public string? SwiftCode       { get; private set; }

    public bool IsActive { get; private set; }

    public virtual FinancialPartyCategory Category { get; private set; } = default!;
    public virtual Account Account                 { get; private set; } = default!;

    private FinancialParty() { }

    public static FinancialParty Create(
        int categoryId, int accountId,
        IReadOnlyDictionary<string, CreditLimitEntry>? creditLimits,
        IReadOnlyList<string>? allowedCurrencies,
        string? phone, string? mobile, string? email,
        string? address, string? contactPerson, string? notes,
        string? bankAccountNumber = null, string? swiftCode = null, string? addressEn = null,
        IReadOnlyDictionary<string, string>? currencyIbans = null)
    {
        return new FinancialParty
        {
            CategoryId       = categoryId,
            AccountId        = accountId,
            CreditLimits     = SerializeCreditLimits(creditLimits),
            AllowedCurrencies = SerializeCurrencies(allowedCurrencies),
            CurrencyIbans    = SerializeCurrencyIbans(currencyIbans),
            Phone            = phone?.Trim(),
            Mobile           = mobile?.Trim(),
            Email            = email?.Trim(),
            Address          = address?.Trim(),
            AddressEn        = addressEn?.Trim(),
            ContactPerson    = contactPerson?.Trim(),
            Notes            = notes?.Trim(),
            BankAccountNumber = string.IsNullOrWhiteSpace(bankAccountNumber) ? null : bankAccountNumber.Trim(),
            SwiftCode        = string.IsNullOrWhiteSpace(swiftCode) ? null : swiftCode.Trim(),
            IsActive         = true,
        };
    }

    public void Update(
        IReadOnlyDictionary<string, CreditLimitEntry>? creditLimits,
        IReadOnlyList<string>? allowedCurrencies,
        string? phone, string? mobile, string? email,
        string? address, string? contactPerson, string? notes,
        string? bankAccountNumber = null, string? swiftCode = null, string? addressEn = null,
        IReadOnlyDictionary<string, string>? currencyIbans = null)
    {
        CreditLimits      = SerializeCreditLimits(creditLimits);
        AllowedCurrencies = SerializeCurrencies(allowedCurrencies);
        CurrencyIbans     = SerializeCurrencyIbans(currencyIbans);
        Phone             = phone?.Trim();
        Mobile            = mobile?.Trim();
        Email             = email?.Trim();
        Address           = address?.Trim();
        AddressEn         = addressEn?.Trim();
        ContactPerson     = contactPerson?.Trim();
        Notes             = notes?.Trim();
        BankAccountNumber = string.IsNullOrWhiteSpace(bankAccountNumber) ? null : bankAccountNumber.Trim();
        SwiftCode         = string.IsNullOrWhiteSpace(swiftCode) ? null : swiftCode.Trim();
    }

    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;

    public List<string> GetAllowedCurrenciesList()
    {
        if (string.IsNullOrWhiteSpace(AllowedCurrencies)) return new List<string>();
        try { return JsonSerializer.Deserialize<List<string>>(AllowedCurrencies) ?? new List<string>(); }
        catch { return new List<string>(); }
    }

    public Dictionary<string, CreditLimitEntry> GetCreditLimits()
    {
        if (string.IsNullOrWhiteSpace(CreditLimits)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, CreditLimitEntry>>(CreditLimits)
                   ?? new Dictionary<string, CreditLimitEntry>();
        }
        catch { return new Dictionary<string, CreditLimitEntry>(); }
    }

    public Dictionary<string, string> GetCurrencyIbans()
    {
        if (string.IsNullOrWhiteSpace(CurrencyIbans)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(CurrencyIbans)
                   ?? new Dictionary<string, string>();
        }
        catch { return new Dictionary<string, string>(); }
    }

    private static string? SerializeCurrencies(IReadOnlyList<string>? list)
    {
        if (list == null || list.Count == 0) return null;
        var clean = list.Where(c => !string.IsNullOrWhiteSpace(c))
                        .Select(c => c.Trim().ToUpperInvariant())
                        .Distinct().ToList();
        return clean.Count > 0 ? JsonSerializer.Serialize(clean) : null;
    }

    private static string? SerializeCreditLimits(IReadOnlyDictionary<string, CreditLimitEntry>? map)
    {
        if (map == null || map.Count == 0) return null;
        // ‎نُطبّع: مفاتيح uppercase، نتجاهل أي صف debit=0 و credit=0 (فارغ).
        var clean = map
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key))
            .Select(kv => new KeyValuePair<string, CreditLimitEntry>(
                kv.Key.Trim().ToUpperInvariant(),
                new CreditLimitEntry(kv.Value.Debit ?? 0m, kv.Value.Credit ?? 0m)))
            .Where(kv => kv.Value.Debit != 0m || kv.Value.Credit != 0m)
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);
        return clean.Count > 0 ? JsonSerializer.Serialize(clean) : null;
    }

    private static string? SerializeCurrencyIbans(IReadOnlyDictionary<string, string>? map)
    {
        if (map == null || map.Count == 0) return null;
        var clean = map
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => new KeyValuePair<string, string>(
                kv.Key.Trim().ToUpperInvariant(),
                kv.Value.Trim().ToUpperInvariant()))
            .GroupBy(kv => kv.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);
        return clean.Count > 0 ? JsonSerializer.Serialize(clean) : null;
    }
}

/// <summary>سقف ائتمان لعملة واحدة: حدّ مدين وحدّ دائن.</summary>
public sealed record CreditLimitEntry(decimal? Debit, decimal? Credit);
