namespace IraqiTradeCenterCompany.Modules.Accounting.Application.Internal;

/// <summary>
/// شروط SQL مشتركة لتقارير المحاسبة (كشف حساب / أرصدة / ميزان مراجعة).
/// عند تفعيل <see cref="IncludeOpeningEntries"/> تُعامَل قيود Opening (EntryType=2)
/// مثل القيود العادية مع احترام تاريخ القيد:
///   • قبل @from → ضمن الافتتاحي
///   • ضمن [@from, @to] → ضمن حركة الفترة
/// </summary>
public static class ReportEntrySqlFragments
{
    public static string OpeningBalanceWhere(bool includeOpeningEntries) =>
        includeOpeningEntries
            ? @"(
        (a.[Type] IN (1,2,3) AND e.EntryDate < @from AND (e.EntryType = 1 OR e.EntryType = 2))
     OR (a.[Type] IN (4,5) AND e.EntryDate < @from AND (e.EntryType = 1 OR e.EntryType = 2)
            AND (@plFyStart IS NULL OR e.EntryDate >= @plFyStart))
      )"
            : @"(
        (a.[Type] IN (1,2,3) AND e.EntryType = 1 AND e.EntryDate < @from)
     OR (a.[Type] IN (4,5) AND e.EntryType = 1 AND e.EntryDate < @from
            AND (@plFyStart IS NULL OR e.EntryDate >= @plFyStart))
      )";

    public static string PeriodEntryTypeFilter(bool includeOpeningEntries) =>
        includeOpeningEntries
            ? "(e.EntryType = 1 OR e.EntryType = 2)"
            : "e.EntryType = 1";

    /// <summary>
    /// ميزان المراجعة / أرصدة الحسابات — الافتتاحي (الفترة السابقة):
    /// يُفعَّل فقط عند <paramref name="includePriorOpening"/> (بداية التقرير بعد بداية السنة المالية =
    /// «الرجوع في التاريخ»). وإلا يُرجع شرطاً فارغاً (قاعدة بيانات فارغة — لا أرصدة مرحّلة).
    /// عند التفعيل: حركات بتاريخ &lt; @from ضمن نفس السنة المالية (من plFyStart).
    /// </summary>
    public static string TrialBalanceOpeningBalanceWhere(bool includeOpeningEntries, bool includePriorOpening)
    {
        if (!includePriorOpening)
            return "1 = 0";

        return includeOpeningEntries
            ? @"(
        e.EntryDate < @from
        AND (@plFyStart IS NULL OR e.EntryDate >= @plFyStart)
        AND (
            (a.[Type] IN (1,2,3) AND (e.EntryType = 1 OR e.EntryType = 2))
         OR (a.[Type] IN (4,5) AND (e.EntryType = 1 OR e.EntryType = 2))
        )
      )"
            : @"(
        e.EntryDate < @from
        AND (@plFyStart IS NULL OR e.EntryDate >= @plFyStart)
        AND e.EntryType = 1
        AND (a.[Type] IN (1,2,3) OR a.[Type] IN (4,5))
      )";
    }

    /// <summary>
    /// كشف حساب: حركات الفترة = قيود Normal فقط. قيود Opening تُعرض منفصلة.
    /// </summary>
    public static string AccountStatementPeriodEntryFilter() => "e.EntryType = 1";

    /// <summary>
    /// كشف حساب: احترام تاريخ البداية الذي اختاره المستخدم (@from) لكل أنواع الحسابات
    /// بما فيها الإيرادات والمصروفات — لا قصّ ببداية السنة المالية plFyStart.
    /// </summary>
    public static string AccountStatementPeriodAccountFilter() =>
        "(a.[Type] IN (1,2,3) OR a.[Type] IN (4,5))";

    /// <summary>
    /// كشف حساب: الرصيد الافتتاحي قبل أول حركة في الفترة.
    /// عند from = بداية السنة: لا أرصدة سابقة؛ قيود Opening في يوم البداية تُعرض كحركات وليس هنا.
    /// عند الرجوع داخل السنة: مثل ميزان المراجعة (من plFyStart حتى قبل @from).
    /// </summary>
    public static string AccountStatementOpeningBalanceWhere(bool includeOpeningEntries, bool includePriorOpening)
    {
        if (includePriorOpening)
            return TrialBalanceOpeningBalanceWhere(includeOpeningEntries, true);

        if (!includeOpeningEntries)
        {
            return @"(
        e.EntryDate < @from
        AND (@plFyStart IS NULL OR e.EntryDate >= @plFyStart)
        AND e.EntryType = 1
        AND (a.[Type] IN (1,2,3) OR a.[Type] IN (4,5))
      )";
        }

        return @"(
        e.EntryDate < @from
        AND (@plFyStart IS NULL OR e.EntryDate >= @plFyStart)
        AND (
            (a.[Type] IN (1,2,3) AND (e.EntryType = 1 OR e.EntryType = 2))
         OR (a.[Type] IN (4,5) AND (e.EntryType = 1 OR e.EntryType = 2))
        )
      )";
    }

    /// <summary>
    /// كشف حساب: قيود Opening المعروضة كحركات (ضمن السنة الحالية فقط — لا قيود سنوات سابقة).
    /// </summary>
    public static string AccountStatementOpeningEntriesWhere() =>
        @"(
        e.EntryType = 2
        AND e.EntryDate <= @to
        AND (@plFyStart IS NULL OR e.EntryDate >= @plFyStart)
      )";
}
