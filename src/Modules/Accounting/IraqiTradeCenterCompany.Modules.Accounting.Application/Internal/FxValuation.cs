namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// منطق تقويم العملات الموحَّد للتقارير (أرصدة الحسابات / الميزان / كشف الحساب).
/// يحسب مُعامِل التحويل من عملة السطر إلى العملة الأساسية، مع أولوية للسعر اليدوي
/// المحفوظ على القيد (إن وُجد) على سعر النشرة المعتمدة.
///
/// قاعدة العملية: 1 = ضرب (Base = Foreign × Rate)، 2 = قسمة (Base = Foreign ÷ Rate).
/// </summary>
public static class FxValuation
{
    /// <summary>
    /// يُعيد مُعامِل تحويل عملة السطر إلى العملة الأساسية.
    ///   • العملة == الأساسية → 1.
    ///   • سعر يدوي صالح على القيد → يُستخدم أولاً وفق عمليته.
    ///   • وإلا سعر النشرة المعتمدة للعملة.
    ///   • وإلا (لا سعر) → 1 مع رفع <paramref name="usedFallback"/> للإشارة لعدم توفّر سعر.
    /// </summary>
    public static decimal Multiplier(
        string? lineCurrency,
        string baseCurrency,
        decimal? manualRate,
        int? manualOperation,
        IReadOnlyDictionary<string, (decimal Rate, int Operation)> rates,
        ref bool usedFallback)
    {
        var b = baseCurrency.Trim().ToUpperInvariant();
        var c = string.IsNullOrWhiteSpace(lineCurrency) ? b : lineCurrency.Trim().ToUpperInvariant();
        if (c == b) return 1m;

        // السعر اليدوي المحفوظ على القيد له الأولوية على النشرة (سعر تاريخي مثبَّت).
        if (manualRate.HasValue && manualRate.Value > 0m)
            return manualOperation == 2 ? 1m / manualRate.Value : manualRate.Value;

        if (rates.TryGetValue(c, out var entry) && entry.Rate > 0)
            return entry.Operation == 2 ? 1m / entry.Rate : entry.Rate;

        usedFallback = true;
        return 1m;
    }
}
