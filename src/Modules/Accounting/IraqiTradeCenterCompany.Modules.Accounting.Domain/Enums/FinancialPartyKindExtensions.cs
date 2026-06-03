namespace IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;

public static class FinancialPartyKindExtensions
{
    /// <summary>مصرف أو شركة دفع — نفس حقول IBAN/SWIFT.</summary>
    public static bool IsBankLike(this FinancialPartyKind kind)
        => kind is FinancialPartyKind.Bank or FinancialPartyKind.PaymentCompany;

    /// <summary>صندوق — بدون تبويب معلومات الاتصال.</summary>
    public static bool HasContactInfo(this FinancialPartyKind kind)
        => kind is not FinancialPartyKind.CashBox;
}
