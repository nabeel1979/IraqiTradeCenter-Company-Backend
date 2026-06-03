using IraqiTradeCenterCompany.SharedKernel.Common;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;

namespace IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;

/// <summary>
/// تسوية بين حسابين — تصفير رصيد بعملة ونقله إلى حساب آخر (نفس العملة أو عملة مختلفة).
/// يُولِّد قيدين: قيد بعملة المصدر (مصدر → وسيط) وقيد بعملة الهدف (وسيط → هدف + فرق عملة).
/// </summary>
public class AccountSettlement : BaseEntity
{
    public string SettlementNumber { get; private set; } = default!;

    public int SourceAccountId { get; private set; }
    public string SourceCurrency { get; private set; } = "IQD";
    public decimal SourceAmount { get; private set; }

    public int TargetAccountId { get; private set; }
    public string TargetCurrency { get; private set; } = "IQD";
    public decimal TargetAmount { get; private set; }

    /// <summary>سعر الصرف المُطبَّق: 1 وحدة مصدر = ExchangeRate وحدة هدف.</summary>
    public decimal ExchangeRate { get; private set; }

    public int SourceTransitAccountId { get; private set; }
    public int TargetTransitAccountId { get; private set; }

    /// <summary>فرق العملة (بالعملة الهدف) — موجب = ربح، سالب = خسارة.</summary>
    public decimal FxGainLossAmount { get; private set; }
    public int? FxGainLossAccountId { get; private set; }

    /// <summary>خصم مُطبَّق لتصفير جزء/كل فرق الصرف (قيمة موجبة).</summary>
    public decimal FxDiscountAmount { get; private set; }
    public int? FxDiscountAccountId { get; private set; }

    public DateTime SettlementDate { get; private set; }
    public string? CancelReason { get; private set; }
    public int? SourceReversalJournalEntryId { get; private set; }
    public int? TargetReversalJournalEntryId { get; private set; }
    public string? Description { get; private set; }

    public int SourceJournalEntryId { get; private set; }
    public int TargetJournalEntryId { get; private set; }

    public virtual Account? SourceAccount { get; private set; }
    public virtual Account? TargetAccount { get; private set; }
    public virtual Account? SourceTransitAccount { get; private set; }
    public virtual Account? TargetTransitAccount { get; private set; }
    public virtual JournalEntry? SourceJournalEntry { get; private set; }
    public virtual JournalEntry? TargetJournalEntry { get; private set; }

    private AccountSettlement() { }

    public static AccountSettlement Create(
        string settlementNumber,
        int sourceAccountId,
        string sourceCurrency,
        decimal sourceAmount,
        int targetAccountId,
        string targetCurrency,
        decimal targetAmount,
        decimal exchangeRate,
        int sourceTransitAccountId,
        int targetTransitAccountId,
        decimal fxGainLossAmount,
        int? fxGainLossAccountId,
        decimal fxDiscountAmount,
        int? fxDiscountAccountId,
        DateTime settlementDate,
        int sourceJournalEntryId,
        int targetJournalEntryId,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(settlementNumber)) throw new DomainException("رقم التسوية مطلوب");
        if (sourceAccountId <= 0 || targetAccountId <= 0) throw new DomainException("الحسابان مطلوبان");
        if (sourceAccountId == targetAccountId && sourceCurrency.Equals(targetCurrency, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("لا يمكن التسوية بين نفس الحساب بنفس العملة");
        if (sourceAmount <= 0 || targetAmount <= 0) throw new DomainException("المبالغ يجب أن تكون موجبة");
        if (sourceTransitAccountId <= 0 || targetTransitAccountId <= 0)
            throw new DomainException("حسابات الوسيط مطلوبة");
        if (sourceJournalEntryId <= 0 || targetJournalEntryId <= 0)
            throw new DomainException("قيود التسوية مطلوبة");

        return new AccountSettlement
        {
            SettlementNumber = settlementNumber.Trim(),
            SourceAccountId = sourceAccountId,
            SourceCurrency = sourceCurrency.Trim().ToUpperInvariant(),
            SourceAmount = sourceAmount,
            TargetAccountId = targetAccountId,
            TargetCurrency = targetCurrency.Trim().ToUpperInvariant(),
            TargetAmount = targetAmount,
            ExchangeRate = exchangeRate,
            SourceTransitAccountId = sourceTransitAccountId,
            TargetTransitAccountId = targetTransitAccountId,
            FxGainLossAmount = fxGainLossAmount,
            FxGainLossAccountId = fxGainLossAccountId,
            FxDiscountAmount = fxDiscountAmount < 0 ? 0 : fxDiscountAmount,
            FxDiscountAccountId = fxDiscountAccountId,
            SettlementDate = settlementDate,
            SourceJournalEntryId = sourceJournalEntryId,
            TargetJournalEntryId = targetJournalEntryId,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        };
    }

    public void MarkAsCancelled(string reason, int sourceReversalEntryId, int targetReversalEntryId, string? by = null)
    {
        if (IsDeleted) throw new DomainException("التسوية ملغاة مسبقاً");
        if (sourceReversalEntryId <= 0 || targetReversalEntryId <= 0)
            throw new DomainException("قيود العكس مطلوبة");
        CancelReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء تسوية" : reason.Trim();
        SourceReversalJournalEntryId = sourceReversalEntryId;
        TargetReversalJournalEntryId = targetReversalEntryId;
        MarkAsDeleted(by);
    }
}
