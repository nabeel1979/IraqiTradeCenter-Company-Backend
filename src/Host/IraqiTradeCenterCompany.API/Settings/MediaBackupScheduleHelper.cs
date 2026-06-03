using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace IraqiTradeCenterCompany.API.Settings;

/// <summary>
/// صيغة الجدولة في <see cref="MediaBackupSettings.AutoBackupCron"/>:
/// <list type="bullet">
///   <item><c>daily@HH:mm</c> أو <c>daily@HH:mm,HH:mm,…</c></item>
///   <item><c>weekly@D@HH:mm</c> أو <c>weekly@D@HH:mm,HH:mm,…</c></item>
///   <item><c>monthly@D@HH:mm</c> أو <c>monthly@D@HH:mm,HH:mm,…</c></item>
/// </list>
/// التوقيت بتوقيت بغداد (UTC+3).
/// </summary>
public static class MediaBackupScheduleHelper
{
    private static readonly TimeZoneInfo BaghdadTz = ResolveBaghdadTimeZone();

    private static readonly Regex DailyRx = new(@"^daily@([\d]{2}:[\d]{2}(?:,[\d]{2}:[\d]{2})*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WeeklyRx = new(@"^weekly@(\d)@([\d]{2}:[\d]{2}(?:,[\d]{2}:[\d]{2})*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MonthlyRx = new(@"^monthly@(\d{1,2})@([\d]{2}:[\d]{2}(?:,[\d]{2}:[\d]{2})*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public enum ScheduleKind { Daily, Weekly, Monthly }

    public sealed class ParsedSchedule
    {
        public ScheduleKind Kind { get; init; }
        public TimeOnly Time { get; init; }
        /// <summary>0–6 للأسبوعي، 1–28 للشهري.</summary>
        public int Day { get; init; }
    }

    public static bool TryParseAll(string? value, out IReadOnlyList<ParsedSchedule> schedules, out string error)
    {
        schedules = Array.Empty<ParsedSchedule>();
        error = string.Empty;
        var s = value?.Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            error = "حدّد جدولة النسخ الاحتياطي (مثال: daily@02:00).";
            return false;
        }

        var m = DailyRx.Match(s);
        if (m.Success)
            return ExpandTimes(ScheduleKind.Daily, 0, m.Groups[1].Value, out schedules, out error);

        m = WeeklyRx.Match(s);
        if (m.Success)
        {
            var dow = int.Parse(m.Groups[1].Value);
            if (dow is < 0 or > 6) { error = "يوم الأسبوع يجب أن يكون بين 0 (الأحد) و 6 (السبت)."; return false; }
            return ExpandTimes(ScheduleKind.Weekly, dow, m.Groups[2].Value, out schedules, out error);
        }

        m = MonthlyRx.Match(s);
        if (m.Success)
        {
            var dom = int.Parse(m.Groups[1].Value);
            if (dom is < 1 or > 28) { error = "يوم الشهر يجب أن يكون بين 1 و 28."; return false; }
            return ExpandTimes(ScheduleKind.Monthly, dom, m.Groups[2].Value, out schedules, out error);
        }

        error = "صيغة الجدولة غير صحيحة — استخدم daily@HH:mm أو daily@HH:mm,HH:mm.";
        return false;
    }

    public static bool TryParse(string? value, out ParsedSchedule schedule, out string error)
    {
        schedule = null!;
        if (!TryParseAll(value, out var all, out error) || all.Count == 0)
        {
            if (string.IsNullOrEmpty(error)) error = "حدّد جدولة واحدة على الأقل.";
            return false;
        }
        schedule = all[0];
        return true;
    }

    public static string DescribeAll(IReadOnlyList<ParsedSchedule> schedules)
    {
        if (schedules.Count == 0) return string.Empty;
        var first = schedules[0];
        var times = string.Join("، ", schedules
            .Select(x => x.Time.ToString("HH:mm", CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(x => x, StringComparer.Ordinal));

        return first.Kind switch
        {
            ScheduleKind.Daily => schedules.Count == 1
                ? $"يومياً الساعة {times} (توقيت بغداد)"
                : $"يومياً الساعة {times} (توقيت بغداد)",
            ScheduleKind.Weekly => $"كل {DayOfWeekName(first.Day)} الساعة {times} (توقيت بغداد)",
            ScheduleKind.Monthly => $"يوم {first.Day} من كل شهر الساعة {times} (توقيت بغداد)",
            _ => times,
        };
    }

    public static string Describe(ParsedSchedule s) => DescribeAll(new[] { s });

    public static bool IsDueNow(string? cron, DateTime utcNow, IReadOnlyCollection<string> firedSlotKeys)
    {
        if (!TryParseAll(cron, out var schedules, out _)) return false;
        return GetCurrentSlotKey(cron, utcNow, firedSlotKeys) != null;
    }

    public static string BuildSlotKey(ParsedSchedule s, DateTime localBaghdad)
    {
        var date = localBaghdad.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = s.Time.ToString("HH:mm", CultureInfo.InvariantCulture);
        return s.Kind switch
        {
            ScheduleKind.Daily => $"daily:{date}@{time}",
            ScheduleKind.Weekly => $"weekly:{date}@{time}@dow{(int)localBaghdad.DayOfWeek}",
            ScheduleKind.Monthly => $"monthly:{localBaghdad:yyyy-MM}@{time}@dom{s.Day}",
            _ => $"{date}@{time}",
        };
    }

    public static string? GetCurrentSlotKey(string? cron, DateTime utcNow, IReadOnlyCollection<string>? firedSlotKeys = null)
    {
        if (!TryParseAll(cron, out var schedules, out _)) return null;

        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, BaghdadTz);
        foreach (var s in schedules)
        {
            if (local.Hour != s.Time.Hour || local.Minute != s.Time.Minute) continue;
            var matches = s.Kind switch
            {
                ScheduleKind.Daily => true,
                ScheduleKind.Weekly => (int)local.DayOfWeek == s.Day,
                ScheduleKind.Monthly => local.Day == s.Day,
                _ => false,
            };
            if (!matches) continue;

            var key = BuildSlotKey(s, local);
            if (firedSlotKeys != null && firedSlotKeys.Contains(key)) continue;
            return key;
        }
        return null;
    }

    public static DateTime? GetNextOccurrenceUtc(string? cron, DateTime utcNow)
    {
        if (!TryParseAll(cron, out var schedules, out _)) return null;

        DateTime? best = null;
        foreach (var s in schedules)
        {
            var next = GetNextOccurrenceUtcForSchedule(s, utcNow);
            if (next.HasValue && (!best.HasValue || next.Value < best.Value))
                best = next;
        }
        return best;
    }

    public static string DefaultCron => "daily@02:00";

    public static string BuildCron(ScheduleKind kind, IEnumerable<TimeOnly> times, int day = 0)
    {
        var list = times
            .Distinct()
            .OrderBy(t => t)
            .Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture))
            .ToList();
        if (list.Count == 0) list.Add("02:00");
        var joined = string.Join(",", list);
        return kind switch
        {
            ScheduleKind.Daily => $"daily@{joined}",
            ScheduleKind.Weekly => $"weekly@{Math.Clamp(day, 0, 6)}@{joined}",
            ScheduleKind.Monthly => $"monthly@{Math.Clamp(day, 1, 28)}@{joined}",
            _ => $"daily@{joined}",
        };
    }

    private static DateTime? GetNextOccurrenceUtcForSchedule(ParsedSchedule s, DateTime utcNow)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, BaghdadTz);
        for (var i = 0; i < 366 * 24 * 60; i++)
        {
            var candidate = local.AddMinutes(i);
            if (candidate <= local) continue;
            if (candidate.Hour != s.Time.Hour || candidate.Minute != s.Time.Minute) continue;

            var matches = s.Kind switch
            {
                ScheduleKind.Daily => true,
                ScheduleKind.Weekly => (int)candidate.DayOfWeek == s.Day,
                ScheduleKind.Monthly => candidate.Day == s.Day,
                _ => false,
            };
            if (!matches) continue;

            var unspecified = DateTime.SpecifyKind(
                new DateTime(candidate.Year, candidate.Month, candidate.Day, candidate.Hour, candidate.Minute, 0),
                DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, BaghdadTz);
        }
        return null;
    }

    private static bool ExpandTimes(ScheduleKind kind, int day, string timesPart, out IReadOnlyList<ParsedSchedule> schedules, out string error)
    {
        schedules = Array.Empty<ParsedSchedule>();
        error = string.Empty;
        var parts = timesPart.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            error = "أضف وقتاً واحداً على الأقل.";
            return false;
        }
        if (parts.Length > 12)
        {
            error = "الحد الأقصى 12 موعداً في الجدولة.";
            return false;
        }

        var list = new List<ParsedSchedule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            var bits = part.Split(':', StringSplitOptions.TrimEntries);
            if (bits.Length != 2
                || !int.TryParse(bits[0], out var h)
                || !int.TryParse(bits[1], out var m)
                || !TryTime(h, m, out var t, out error))
                return false;

            var key = t.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (!seen.Add(key)) continue;
            list.Add(new ParsedSchedule { Kind = kind, Time = t, Day = day });
        }

        if (list.Count == 0)
        {
            error = "أضف وقتاً واحداً على الأقل.";
            return false;
        }

        schedules = list.OrderBy(x => x.Time).ToList();
        return true;
    }

    private static bool TryTime(int hour, int minute, out TimeOnly time, out string error)
    {
        error = string.Empty;
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            time = default;
            error = "الوقت غير صحيح — استخدم HH:mm بين 00:00 و 23:59.";
            return false;
        }
        time = new TimeOnly(hour, minute);
        return true;
    }

    private static string DayOfWeekName(int dow) => dow switch
    {
        0 => "الأحد",
        1 => "الاثنين",
        2 => "الثلاثاء",
        3 => "الأربعاء",
        4 => "الخميس",
        5 => "الجمعة",
        6 => "السبت",
        _ => dow.ToString(CultureInfo.InvariantCulture),
    };

    private static TimeZoneInfo ResolveBaghdadTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Baghdad"); }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("Iraq", TimeSpan.FromHours(3), "Iraq", "Iraq");
            }
        }
    }
}
