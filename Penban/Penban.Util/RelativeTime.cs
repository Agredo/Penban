using System.Globalization;

namespace Penban.Util;

/// <summary>
/// Turns a stored UTC timestamp into the short "when was this last touched" caption of the board
/// overview: recent edits read as a duration, yesterday and the last week as a day, anything older
/// as a plain date. Framework-agnostic on purpose - the wording comes from the shared resources, so
/// it follows the active culture.
/// </summary>
public static class RelativeTime
{
    /// <summary>Age of a moment, counted back from <paramref name="nowUtc"/>.</summary>
    public static string Describe(DateTimeOffset momentUtc, DateTimeOffset nowUtc)
    {
        var elapsed = nowUtc - momentUtc;
        if (elapsed < TimeSpan.Zero)
        {
            // Clock skew or a timestamp from a device slightly ahead of this one.
            elapsed = TimeSpan.Zero;
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return Strings.TimeJustNow;
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.TimeMinutesAgoFormat, (int)elapsed.TotalMinutes);
        }

        // Days are counted in local time so "yesterday" means yesterday for the user, not 24 hours back.
        var days = (nowUtc.ToLocalTime().Date - momentUtc.ToLocalTime().Date).Days;
        if (days == 0)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.TimeHoursAgoFormat, (int)elapsed.TotalHours);
        }

        if (days == 1)
        {
            return Strings.TimeYesterday;
        }

        if (days < 7)
        {
            return string.Format(CultureInfo.CurrentCulture, Strings.TimeDaysAgoFormat, days);
        }

        return momentUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }
}
