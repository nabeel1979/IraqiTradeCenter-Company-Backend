namespace IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;

public enum FinancialPartyKind
{
    Supplier       = 1,
    Customer       = 2,
    Bank           = 3,
    /// <summary>صندوق نقدي — يُدار من الإدارة المالية بدل شاشة الصناديق المنفصلة.</summary>
    CashBox        = 4,
    /// <summary>شركة دفع — بطاقة مطابقة للمصرف (IBAN/SWIFT لكل عملة).</summary>
    PaymentCompany = 5,
}
