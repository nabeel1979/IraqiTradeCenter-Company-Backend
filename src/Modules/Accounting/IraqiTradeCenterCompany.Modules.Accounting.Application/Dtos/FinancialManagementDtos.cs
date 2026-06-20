using System.Text.Json.Serialization;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Dtos;

public class FinancialPartyCategoryDto
{
    public int Id                    { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FinancialPartyKind Kind   { get; set; }
    public string NameAr             { get; set; } = default!;
    public string? NameEn            { get; set; }
    public int MainAccountId         { get; set; }
    public string MainAccountCode    { get; set; } = default!;
    public string MainAccountNameAr  { get; set; } = default!;
    public string? MainAccountNameEn { get; set; }
    public bool IsActive             { get; set; }
    public int DisplayOrder          { get; set; }
    public int PartyCount            { get; set; }
}

public class EligibleAccountDto
{
    public int Id          { get; set; }
    public string Code     { get; set; } = default!;
    public string NameAr   { get; set; } = default!;
    public string? NameEn  { get; set; }
}

public class CreditLimitDto
{
    public decimal? Debit  { get; set; }
    public decimal? Credit { get; set; }
}

public class FinancialPartyDto
{
    public int Id                     { get; set; }
    public int CategoryId             { get; set; }
    public string CategoryNameAr      { get; set; } = default!;
    public string? CategoryNameEn     { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FinancialPartyKind Kind    { get; set; }
    /// <summary>اسم الطرف — مصدره الوحيد هو الحساب المرتبط (مزامنة كاملة).</summary>
    public string NameAr              { get; set; } = default!;
    public string? NameEn             { get; set; }
    public int AccountId              { get; set; }
    public string AccountCode         { get; set; } = default!;
    /// <summary>سقوف الائتمان مفهرسة برمز العملة (مدين/دائن).</summary>
    public Dictionary<string, CreditLimitDto> CreditLimits { get; set; } = new();
    public List<string> AllowedCurrencies { get; set; } = new();
    /// <summary>IBAN لكل عملة — خاص بأطراف نوع المصرف.</summary>
    public Dictionary<string, string> CurrencyIbans { get; set; } = new();
    public string? Phone              { get; set; }
    public string? Mobile             { get; set; }
    public string? Email              { get; set; }
    public string? Address            { get; set; }
    public string? AddressEn          { get; set; }
    public string? ContactPerson      { get; set; }
    public string? Notes              { get; set; }
    /// <summary>رقم الحساب المصرفي — خاص بأطراف نوع المصرف.</summary>
    public string? BankAccountNumber  { get; set; }
    /// <summary>رمز السويفت (SWIFT/BIC) — خاص بأطراف نوع المصرف.</summary>
    public string? SwiftCode          { get; set; }
    /// <summary>تفعيل نسبة خصم مبيعات افتراضية تُجلب في الفاتورة.</summary>
    public bool SalesDiscountEnabled  { get; set; }
    /// <summary>نسبة خصم المبيعات الافتراضية (%).</summary>
    public decimal SalesDiscountPercentage { get; set; }
    public bool IsActive              { get; set; }
    public DateTime CreatedAt         { get; set; }
}
