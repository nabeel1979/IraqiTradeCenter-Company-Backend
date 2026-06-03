using System.Globalization;
using IraqiTradeCenterCompany.Modules.Accounting.Domain.Enums;

namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// عرض سعر الصرف بصيغة النشرة: «1500 * 1» أو «1500 / 1».
/// </summary>
internal static class SettlementRateDisplay
{
    public static string FormatBulletinLine(decimal rate, CurrencyRateOperation operation)
    {
        if (rate <= 0) return "—";
        var r = FormatRateNumber(rate);
        return operation == CurrencyRateOperation.Divide ? $"{r} / 1" : $"{r} * 1";
    }

    /// <summary>
    /// يعرض سطر النشرة للعملة الأجنبية في الزوج (كما مكتوب في النشرة).
    /// </summary>
    public static string FormatForCurrencyPair(
        string from,
        string to,
        string baseCur,
        IReadOnlyDictionary<string, (decimal Rate, int Operation)> rates)
    {
        var src = from.Trim().ToUpperInvariant();
        var tgt = to.Trim().ToUpperInvariant();
        var bas = baseCur.Trim().ToUpperInvariant();
        if (src == tgt) return "1 * 1";

        if (tgt != bas && rates.TryGetValue(tgt, out var toLine) && toLine.Rate > 0)
            return FormatBulletinLine(toLine.Rate, (CurrencyRateOperation)toLine.Operation);

        if (src != bas && rates.TryGetValue(src, out var fromLine) && fromLine.Rate > 0)
            return FormatBulletinLine(fromLine.Rate, (CurrencyRateOperation)fromLine.Operation);

        return FormatFromCrossRate(CrossRate(src, tgt, bas, rates));
    }

    /// <summary>
    /// يحوّل سعر الصرف العشري (1 مصدر = X هدف) إلى صيغة نشرة.
    /// </summary>
    public static string FormatFromCrossRate(decimal crossRate)
    {
        if (crossRate <= 0) return "—";
        if (Math.Abs(crossRate - 1m) < 0.0000001m) return "1 * 1";
        if (crossRate >= 1m)
            return $"{FormatRateNumber(crossRate)} * 1";
        return $"{FormatRateNumber(1m / crossRate)} / 1";
    }

    private static decimal CrossRate(
        string from,
        string to,
        string baseCur,
        IReadOnlyDictionary<string, (decimal Rate, int Operation)> rates)
    {
        if (from.Equals(to, StringComparison.OrdinalIgnoreCase)) return 1m;
        var fromMult = ToBaseMultiplier(from, baseCur, rates);
        var toMult = ToBaseMultiplier(to, baseCur, rates);
        if (toMult == 0) return 0;
        return fromMult / toMult;
    }

    private static decimal ToBaseMultiplier(
        string ccy,
        string baseCur,
        IReadOnlyDictionary<string, (decimal Rate, int Operation)> rates)
    {
        var c = ccy.Trim().ToUpperInvariant();
        var b = baseCur.Trim().ToUpperInvariant();
        if (c == b) return 1m;
        if (!rates.TryGetValue(c, out var e) || e.Rate <= 0) return 1m;
        return e.Operation == (int)CurrencyRateOperation.Divide ? 1m / e.Rate : e.Rate;
    }

    private static string FormatRateNumber(decimal rate)
    {
        var rounded = Math.Round(rate, 6);
        if (rounded == decimal.Truncate(rounded))
            return ((long)rounded).ToString(CultureInfo.InvariantCulture);
        return rounded.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
