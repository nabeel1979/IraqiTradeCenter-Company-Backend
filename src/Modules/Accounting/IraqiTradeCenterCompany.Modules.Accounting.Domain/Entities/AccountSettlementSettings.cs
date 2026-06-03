using IraqiTradeCenterCompany.SharedKernel.Common;

namespace IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;

/// <summary>
/// إعدادات تسوية الحسابات (صف واحد Id=1).
/// TransitAccountsJson: {"IQD":123,"USD":456} — حساب وسيط لكل عملة (مُستبعد من الكشوفات).
/// </summary>
public class AccountSettlementSettings : BaseEntity
{
    public const int SingletonId = 1;

    /// <summary>JSON: currency code → account id</summary>
    public string? TransitAccountsJson { get; private set; }
    public int? FxGainAccountId { get; private set; }
    public int? FxLossAccountId { get; private set; }
    /// <summary>حساب خصم فرق الصرف — يُستخدم لتصفير فرق العملة بدلاً من أرباح/خسائر.</summary>
    public int? FxDiscountAccountId { get; private set; }

    private AccountSettlementSettings() { }

    public static AccountSettlementSettings CreateDefault()
        => new() { Id = SingletonId };

    public void Update(string? transitAccountsJson, int? fxGainAccountId, int? fxLossAccountId, int? fxDiscountAccountId)
    {
        TransitAccountsJson = string.IsNullOrWhiteSpace(transitAccountsJson) ? null : transitAccountsJson.Trim();
        FxGainAccountId = fxGainAccountId;
        FxLossAccountId = fxLossAccountId;
        FxDiscountAccountId = fxDiscountAccountId;
    }
}
