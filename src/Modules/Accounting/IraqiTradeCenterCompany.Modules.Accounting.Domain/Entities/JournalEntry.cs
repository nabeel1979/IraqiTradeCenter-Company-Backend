using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Exceptions;
using IraqiTradeCenterCompany.SharedKernel.Common;
using IraqiTradeCenterCompany.SharedKernel.Exceptions;

namespace IraqiTradeCenterCompany.Modules.Accounting.Domain.Entities;

public class JournalEntry : BaseEntity
{
    public string EntryNumber { get; private set; } = default!;
    public DateTime EntryDate { get; private set; }
    public int FiscalYearId { get; private set; }
    public int AccountingPeriodId { get; private set; }
    public JournalEntryStatus Status { get; private set; }
    public JournalEntrySource Source { get; private set; }
    public JournalEntryType EntryType { get; private set; } = JournalEntryType.Normal;
    /// <summary>نوع السند المستخدم في إنشاء هذا القيد (سند قبض، سند دفع، …) — اختياري</summary>
    public int? VoucherTypeId { get; private set; }
    public virtual JournalVoucherType? VoucherType { get; private set; }
    /// <summary>
    /// رقم تسلسلي مخصّص لكل نوع سند (يبدأ من 1 لكل VoucherType على حدة).
    /// عند العرض في الواجهة: {VoucherType.Code}-{VoucherSequence}  مثل "PV-1", "RV-1".
    /// يبقى NULL للقيود اليدوية التي لا ترتبط بنوع سند.
    /// </summary>
    public int? VoucherSequence { get; private set; }
    /// <summary>
    /// رقم يدوي اختياري يُدخله المستخدم لربط القيد بمستند خارجي (شيك، إيصال
    /// خارجي، فاتورة شريك، …). مستقل عن <c>EntryNumber</c> (المسلسل الداخلي)
    /// و<c>VoucherSequence</c> (المسلسل لكل نوع سند) و<c>ReferenceNumber</c>
    /// (مرجع نظامي يُولَّد من فاتورة/مناقلة). قابل للبحث في صفحة القيود.
    /// </summary>
    public string? ManualNumber { get; private set; }
    public string Currency { get; private set; } = "IQD";
    /// <summary>
    /// سعر صرف يدوي اختياري يُحفَظ على القيد. يُستخدم لتقويم القيد عند غياب نشرة
    /// تُسعّر عملته بتاريخه، أو لتثبيت سعر تاريخي خاص بهذا القيد. NULL = استخدام
    /// سعر النشرة المعتمدة عند التقويم.
    /// </summary>
    public decimal? ManualExchangeRate { get; private set; }
    /// <summary>عملية السعر اليدوي: 1=ضرب (Base=Foreign×Rate)، 2=قسمة (Base=Foreign÷Rate).</summary>
    public int? ManualExchangeRateOperation { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal TotalDebit { get; private set; }
    public decimal TotalCredit { get; private set; }
    public string? ReferenceType { get; private set; }
    public int? ReferenceId { get; private set; }
    public string? ReferenceNumber { get; private set; }
    public DateTime? PostedAt { get; private set; }
    public string? PostedBy { get; private set; }
    public int? ReversedByEntryId { get; private set; }
    public virtual ICollection<JournalEntryLine> Lines { get; private set; } = new List<JournalEntryLine>();

    private JournalEntry() { }

    public static JournalEntry Create(DateTime date, int fyId, int periodId, JournalEntrySource source,
                                       string description, string? refType = null, int? refId = null, string? refNumber = null,
                                       JournalEntryType type = JournalEntryType.Normal, string currency = "IQD",
                                       string? entryNumber = null, int? voucherTypeId = null,
                                       int? voucherSequence = null, string? manualNumber = null,
                                       decimal? manualExchangeRate = null, int? manualExchangeRateOperation = null)
    {
        if (string.IsNullOrWhiteSpace(entryNumber))
            throw new DomainException("رقم القيد مطلوب");

        var (mxr, mxrOp) = NormalizeManualRate(manualExchangeRate, manualExchangeRateOperation);

        return new JournalEntry
        {
            EntryNumber = entryNumber.Trim(),
            EntryDate = date, FiscalYearId = fyId, AccountingPeriodId = periodId,
            Status = JournalEntryStatus.Draft, Source = source,
            EntryType = type,
            VoucherTypeId = voucherTypeId,
            VoucherSequence = voucherSequence,
            ManualNumber = NormalizeManualNumber(manualNumber),
            Currency = string.IsNullOrWhiteSpace(currency) ? "IQD" : currency.Trim().ToUpperInvariant(),
            ManualExchangeRate = mxr,
            ManualExchangeRateOperation = mxrOp,
            Description = string.IsNullOrWhiteSpace(description) ? "—" : description.Trim(),
            ReferenceType = refType, ReferenceId = refId, ReferenceNumber = refNumber
        };
    }

    /// <summary>
    /// يطبّع سعر الصرف اليدوي: قيمة ≤ 0 → null. العملية تُقصَر على 1 (ضرب) أو
    /// 2 (قسمة)، وتُعاد null تماماً عند غياب السعر لتفادي بيانات يتيمة.
    /// </summary>
    private static (decimal? Rate, int? Operation) NormalizeManualRate(decimal? rate, int? operation)
    {
        if (!rate.HasValue || rate.Value <= 0m) return (null, null);
        var op = operation == 2 ? 2 : 1;
        return (rate.Value, op);
    }

    /// <summary>
    /// يطبّق التطبيع على الرقم اليدوي: trim + تحويل الفراغ → null. الحدّ الأعلى
    /// المُطبَّق هنا تحفّظي (50)؛ EF Configuration يحدّد العمود بنفس الحدّ.
    /// </summary>
    private static string? NormalizeManualNumber(string? raw)
    {
        var s = raw?.Trim();
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (s.Length > 50) s = s[..50];
        return s;
    }

    /// <summary>تحديث الرقم اليدوي فقط (يُسمح حتى للقيود المرحَّلة لأنه بيانات وصفية).</summary>
    public void UpdateManualNumber(string? manualNumber)
    {
        if (Status == JournalEntryStatus.Reversed) throw new DomainException("لا يمكن تعديل قيد معكوس");
        ManualNumber = NormalizeManualNumber(manualNumber);
    }

    public void AddDebit(int accountId, decimal amount, string? desc = null)
    {
        EnsureDraft();
        Lines.Add(JournalEntryLine.CreateDebit(accountId, amount, desc));
        Recalc();
    }

    public void AddCredit(int accountId, decimal amount, string? desc = null)
    {
        EnsureDraft();
        Lines.Add(JournalEntryLine.CreateCredit(accountId, amount, desc));
        Recalc();
    }

    public void UpdateBasic(DateTime entryDate, string description, JournalEntryType type, string currency,
                            int? voucherTypeId = null, string? manualNumber = null,
                            decimal? manualExchangeRate = null, int? manualExchangeRateOperation = null)
    {
        if (Status == JournalEntryStatus.Reversed) throw new DomainException("لا يمكن تعديل قيد معكوس");
        EntryDate = entryDate;
        Description = string.IsNullOrWhiteSpace(description) ? "—" : description.Trim();
        EntryType = type;
        VoucherTypeId = voucherTypeId;
        ManualNumber = NormalizeManualNumber(manualNumber);
        Currency = string.IsNullOrWhiteSpace(currency) ? "IQD" : currency.Trim().ToUpperInvariant();
        var (mxr, mxrOp) = NormalizeManualRate(manualExchangeRate, manualExchangeRateOperation);
        ManualExchangeRate = mxr;
        ManualExchangeRateOperation = mxrOp;
    }

    /// <summary>إزالة ربط نوع السند — يُستخدم عند حذف أنواع نظامية داخلية معطّلة.</summary>
    public void DetachVoucherType()
    {
        VoucherTypeId = null;
        VoucherSequence = null;
    }

    public void ReplaceLines(IReadOnlyList<(int AccountId, bool IsDebit, decimal Amount, string? Description)> newLines)
    {
        if (Status == JournalEntryStatus.Reversed) throw new DomainException("لا يمكن تعديل قيد معكوس");
        Lines.Clear();
        foreach (var l in newLines)
        {
            if (l.IsDebit) Lines.Add(JournalEntryLine.CreateDebit(l.AccountId, l.Amount, l.Description));
            else Lines.Add(JournalEntryLine.CreateCredit(l.AccountId, l.Amount, l.Description));
        }
        Recalc();
    }

    public void Unpost()
    {
        if (Status == JournalEntryStatus.Reversed) throw new DomainException("لا يمكن إلغاء ترحيل قيد معكوس");
        Status = JournalEntryStatus.Draft;
        PostedAt = null;
        PostedBy = null;
    }

    public void Post(string postedBy)
    {
        if (Status != JournalEntryStatus.Draft) throw new DomainException("القيد ليس بحالة مسودة");
        if (Lines.Count < 2) throw new DomainException("القيد لازم سطرين على الأقل");
        Recalc();
        if (Math.Round(TotalDebit, 3) != Math.Round(TotalCredit, 3))
            throw new UnbalancedJournalEntryException(TotalDebit, TotalCredit);
        Status = JournalEntryStatus.Posted;
        PostedAt = DateTime.UtcNow;
        PostedBy = postedBy;
    }

    public JournalEntry CreateReversal(string reason, string entryNumber)
    {
        if (Status != JournalEntryStatus.Posted) throw new DomainException("لا يمكن عكس قيد غير مرحّل");
        if (string.IsNullOrWhiteSpace(entryNumber)) throw new DomainException("رقم القيد العكسي مطلوب");

        var rev = new JournalEntry
        {
            EntryNumber = entryNumber.Trim(),
            EntryDate = DateTime.UtcNow.Date,
            FiscalYearId = FiscalYearId, AccountingPeriodId = AccountingPeriodId,
            Status = JournalEntryStatus.Draft, Source = JournalEntrySource.Manual,
            Description = $"عكس قيد رقم {EntryNumber} - {reason}",
            ReferenceType = "ReversalOf", ReferenceId = Id
        };
        foreach (var l in Lines)
        {
            if (l.IsDebit) rev.AddCredit(l.AccountId, l.Amount, $"عكس: {l.Description}");
            else rev.AddDebit(l.AccountId, l.Amount, $"عكس: {l.Description}");
        }
        return rev;
    }

    public void MarkAsReversed(int revId) { Status = JournalEntryStatus.Reversed; ReversedByEntryId = revId; }

    private void EnsureDraft() { if (Status != JournalEntryStatus.Draft) throw new DomainException("لا يمكن تعديل قيد مرحّل"); }
    private void Recalc()
    {
        TotalDebit = Lines.Where(l => l.IsDebit).Sum(l => l.Amount);
        TotalCredit = Lines.Where(l => !l.IsDebit).Sum(l => l.Amount);
    }
}
