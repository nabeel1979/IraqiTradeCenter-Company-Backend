using IraqiTradeCenterCompany.Modules.Accounting.Application.Persistence;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// حارس تسعير العملة عند تسجيل/تعديل القيود والسندات.
/// العملة الأجنبية يجب أن تكون مُسعَّرة في نشرة منشورة سارية بتاريخ القيد، أو أن
/// يُدخل المستخدم سعر صرف يدوياً. العملة الأساسية لا تحتاج تسعيراً.
/// </summary>
public static class CurrencyBulletinGuard
{
    /// <summary>
    /// يُعيد رسالة خطأ إذا تعذّر تسعير العملة، أو <c>null</c> إذا كان كل شيء سليماً.
    /// </summary>
    public static async Task<string?> CheckAsync(
        IAccountingDbContext db,
        string? currency,
        DateTime entryDate,
        decimal? manualExchangeRate,
        CancellationToken ct)
    {
        var ccy = string.IsNullOrWhiteSpace(currency) ? "IQD" : currency.Trim().ToUpperInvariant();
        var asOf = entryDate.Date.AddDays(1).AddTicks(-1);

        // أحدث نشرة منشورة سارية حتى تاريخ القيد (تحدّد العملة الأساسية والأسعار المتاحة).
        var bulletin = await db.CurrencyRateBulletins
            .AsNoTracking()
            .Include(b => b.Lines)
            .Where(b => b.Status == CurrencyRateBulletinStatus.Published && b.EffectiveAt <= asOf)
            .OrderByDescending(b => b.EffectiveAt).ThenByDescending(b => b.Id)
            .FirstOrDefaultAsync(ct);

        var baseCur = (bulletin?.BaseCurrency ?? "IQD").Trim().ToUpperInvariant();

        // العملة الأساسية لا تحتاج تسعيراً.
        if (ccy == baseCur) return null;

        // سعر صرف يدوي صريح يكفي للحفظ حتى لو لم تُسعِّر النشرة هذه العملة.
        if (manualExchangeRate.HasValue && manualExchangeRate.Value > 0m) return null;

        // وجود سطر يُسعّر العملة في النشرة المعتمدة.
        var priced = bulletin?.Lines.Any(l =>
            l.Rate > 0m &&
            string.Equals(l.Currency, ccy, StringComparison.OrdinalIgnoreCase)) ?? false;
        if (priced) return null;

        return $"العملة {ccy} غير مُسعَّرة في نشرة أسعار منشورة سارية بتاريخ {entryDate:yyyy-MM-dd}. " +
               "انشر نشرة أسعار تتضمّن هذه العملة، أو أدخل سعر صرف يدوياً للقيد.";
    }
}
