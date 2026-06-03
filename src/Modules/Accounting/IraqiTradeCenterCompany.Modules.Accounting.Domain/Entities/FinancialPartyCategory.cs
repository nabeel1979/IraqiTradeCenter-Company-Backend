using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.SharedKernel.Common;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;

namespace IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;

/// <summary>
/// نوع الطرف المالي: مورد، عميل، أو مصرف.
/// كل نوع مرتبط بحساب رئيسي في شجرة الحسابات.
/// الحسابات الفرعية لأعضاء هذا النوع تُنشأ تلقائياً.
/// </summary>
public class FinancialPartyCategory : BaseEntity
{
    public FinancialPartyKind Kind       { get; private set; }
    public string NameAr                 { get; private set; } = default!;
    public string? NameEn                { get; private set; }
    public int MainAccountId             { get; private set; }
    public bool IsActive                 { get; private set; }
    public int DisplayOrder              { get; private set; }

    public virtual Account MainAccount              { get; private set; } = default!;
    public virtual ICollection<FinancialParty> Parties { get; private set; } = new List<FinancialParty>();

    private FinancialPartyCategory() { }

    public static FinancialPartyCategory Create(
        FinancialPartyKind kind, string nameAr, string? nameEn, int mainAccountId, int displayOrder = 100)
    {
        if (string.IsNullOrWhiteSpace(nameAr)) throw new DomainException("اسم النوع مطلوب");
        return new FinancialPartyCategory
        {
            Kind         = kind,
            NameAr       = nameAr.Trim(),
            NameEn       = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim(),
            MainAccountId = mainAccountId,
            IsActive     = true,
            DisplayOrder = displayOrder,
        };
    }

    public void Update(string nameAr, string? nameEn)
    {
        if (string.IsNullOrWhiteSpace(nameAr)) throw new DomainException("اسم النوع مطلوب");
        NameAr = nameAr.Trim();
        NameEn = string.IsNullOrWhiteSpace(nameEn) ? null : nameEn.Trim();
    }

    public void SetDisplayOrder(int order) => DisplayOrder = order;
    public void Activate()   => IsActive = true;
    public void Deactivate() => IsActive = false;
}
