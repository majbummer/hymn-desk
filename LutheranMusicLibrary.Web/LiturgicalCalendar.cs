using System;

/// <summary>Classifies a person into a broad historical era from their birth year.</summary>
public static class PersonEra
{
    public static string GetEra(int birthYear)
    {
        if (birthYear <= 0) return "";
        if (birthYear < 500) return "Early Church";
        if (birthYear < 1500) return "Medieval";
        if (birthYear < 1650) return "Reformation";
        if (birthYear < 1750) return "Baroque";
        if (birthYear < 1830) return "Classical";
        if (birthYear < 1900) return "Romantic";
        if (birthYear < 1950) return "Modern";
        return "Contemporary";
    }

    public static readonly string[] AllEras =
    {
        "Early Church", "Medieval", "Reformation", "Baroque", "Classical", "Romantic", "Modern", "Contemporary",
    };
}

public class WheelSegment
{
    public string Season { get; set; } = "";
    public DateOnly Start { get; set; }
    public DateOnly End { get; set; }
    public string ColorHex { get; set; } = "";
    public string PathData { get; set; } = "";
    public double LabelX { get; set; }
    public double LabelY { get; set; }
    public bool ShowLabel { get; set; }
}

/// <summary>
/// Computes the current liturgical season from a calendar date, using the
/// Meeus/Jones/Butcher algorithm for the Gregorian Easter date. Season names
/// returned here match the `name` column in the church_year_seasons table
/// exactly, so callers can join straight against the database for color,
/// description, etc.
/// </summary>
public static class LiturgicalCalendar
{
    public static string GetSeasonName(DateOnly today)
    {
        int y = today.Year;

        // Christmastide that spans the new year (Dec 25 of last year – Jan 5)
        if (today.Month == 1 && today.Day <= 5)
            return "Christmas";

        var easter = ComputeEaster(y);
        var ashWednesday = easter.AddDays(-46);
        var palmSunday = easter.AddDays(-7);
        var holySaturday = easter.AddDays(-1);
        var pentecost = easter.AddDays(49);

        if (today < ashWednesday) return "Epiphany";           // Jan 6 .. day before Ash Wed
        if (today < palmSunday) return "Lent";                  // Ash Wed .. day before Palm Sunday
        if (today <= holySaturday) return "Holy Week";           // Palm Sunday .. Holy Saturday
        if (today < pentecost) return "Easter";                  // Easter Sunday .. day before Pentecost
        if (today == pentecost) return "Pentecost";               // Pentecost Sunday itself

        // Past Pentecost: either the long green season, Advent, or one of the
        // two fixed autumn observances that fall within it.
        var advent1 = FirstSundayOfAdvent(y);
        if (today >= advent1)
            return today.Month == 12 && today.Day >= 25 ? "Christmas" : "Advent";

        if (today.Month == 10 && today.Day == 31) return "Reformation";
        if (today.Month == 11 && today.Day == 1) return "All Saints";

        return "Time after Pentecost";
    }

    /// <summary>
    /// The Revised Common Lectionary's 3-year cycle (A/B/C) for the church year
    /// currently underway. Year A began Advent 2022; the cycle repeats every 3 years.
    /// </summary>
    public static string GetCurrentLectionaryYear(DateOnly today)
    {
        int adventStartYear = today.Year;
        var advent1ThisYear = FirstSundayOfAdvent(today.Year);
        if (today < advent1ThisYear) adventStartYear -= 1;
        int offset = ((adventStartYear - 2022) % 3 + 3) % 3;
        return offset switch { 0 => "A", 1 => "B", _ => "C" };
    }

    /// <summary>
    /// Walks every day of the given calendar year and groups them into contiguous
    /// season segments, by reusing GetSeasonName day by day. This naturally handles
    /// the Christmas wrap-around at the year boundary correctly for a circular wheel.
    /// </summary>
    public static List<(string Season, DateOnly Start, DateOnly End)> GetYearWheel(int year)
    {
        var result = new List<(string, DateOnly, DateOnly)>();
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        string? currentSeason = null;
        var segStart = start;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var season = GetSeasonName(d);
            if (currentSeason == null)
            {
                currentSeason = season;
            }
            else if (season != currentSeason)
            {
                result.Add((currentSeason, segStart, d.AddDays(-1)));
                currentSeason = season;
                segStart = d;
            }
        }
        result.Add((currentSeason!, segStart, end));
        return result;
    }

    /// <summary>Builds the SVG donut-wedge geometry for a full year's liturgical seasons.</summary>
    public static List<WheelSegment> GetWheelSegments(int year, double cx, double cy, double rOuter, double rInner)
    {
        var segments = GetYearWheel(year);
        var totalDays = (segments.Last().End.DayNumber - segments.First().Start.DayNumber) + 1;
        var results = new List<WheelSegment>();
        double cumulativeDays = 0;

        foreach (var (season, start, end) in segments)
        {
            var daysInSeg = (end.DayNumber - start.DayNumber) + 1;
            var startAngle = cumulativeDays / totalDays * 360.0;
            var endAngle = (cumulativeDays + daysInSeg) / totalDays * 360.0;
            cumulativeDays += daysInSeg;

            results.Add(new WheelSegment
            {
                Season = season,
                Start = start,
                End = end,
                ColorHex = AccentHex(season),
                PathData = BuildDonutPath(cx, cy, rOuter, rInner, startAngle, endAngle),
                LabelX = cx + (rOuter + rInner) / 2 * Math.Sin(DegToRad((startAngle + endAngle) / 2)),
                LabelY = cy - (rOuter + rInner) / 2 * Math.Cos(DegToRad((startAngle + endAngle) / 2)),
                ShowLabel = daysInSeg >= 14,
            });
        }
        return results;
    }

    /// <summary>The angle (degrees, 0 = top, clockwise) of a given date within its year, for the "today" marker.</summary>
    public static double GetDateAngle(DateOnly date)
    {
        var start = new DateOnly(date.Year, 1, 1);
        var end = new DateOnly(date.Year, 12, 31);
        var totalDays = (end.DayNumber - start.DayNumber) + 1;
        var dayOffset = date.DayNumber - start.DayNumber;
        return (double)dayOffset / totalDays * 360.0;
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180.0;

    private static string BuildDonutPath(double cx, double cy, double rOuter, double rInner, double startDeg, double endDeg)
    {
        var largeArc = (endDeg - startDeg) > 180 ? 1 : 0;
        var (x1o, y1o) = PointOnCircle(cx, cy, rOuter, startDeg);
        var (x2o, y2o) = PointOnCircle(cx, cy, rOuter, endDeg);
        var (x1i, y1i) = PointOnCircle(cx, cy, rInner, startDeg);
        var (x2i, y2i) = PointOnCircle(cx, cy, rInner, endDeg);
        return $"M {x1o:F2} {y1o:F2} A {rOuter:F2} {rOuter:F2} 0 {largeArc} 1 {x2o:F2} {y2o:F2} " +
               $"L {x2i:F2} {y2i:F2} A {rInner:F2} {rInner:F2} 0 {largeArc} 0 {x1i:F2} {y1i:F2} Z";
    }

    private static (double, double) PointOnCircle(double cx, double cy, double r, double deg)
    {
        var rad = DegToRad(deg);
        return (cx + r * Math.Sin(rad), cy - r * Math.Cos(rad));
    }

    /// <summary>First Sunday of Advent: the Sunday on or after November 27.</summary>
    private static DateOnly FirstSundayOfAdvent(int year)
    {
        var nov27 = new DateOnly(year, 11, 27);
        int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)nov27.DayOfWeek + 7) % 7;
        return nov27.AddDays(daysUntilSunday);
    }

    /// <summary>Gregorian Easter Sunday via the Meeus/Jones/Butcher algorithm.</summary>
    private static DateOnly ComputeEaster(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// A muted, illuminated-manuscript take on each liturgical color, chosen to
    /// sit alongside the site's parchment/oxblood/gold palette rather than
    /// clash with primary red/green/purple.
    /// </summary>
    public static string AccentHex(string seasonName) => seasonName switch
    {
        "Advent" => "#3E5470",
        "Christmas" => "#B8923D",
        "Epiphany" => "#3F6B5C",
        "Lent" => "#5B3A5C",
        "Holy Week" => "#7A1F1F",
        "Easter" => "#C9A227",
        "Pentecost" => "#A62639",
        "Time after Pentecost" => "#5C6B47",
        "Reformation" => "#8B1E3F",
        "All Saints" => "#9C7A3C",
        _ => "#9C7A3C",
    };

    /// <summary>Search tags to try (in order) against the `seasons` table for a given season name.</summary>
    public static string[] SearchTagsFor(string seasonName) => seasonName switch
    {
        "Time after Pentecost" => new[] { "Ordinary Time", "General use" },
        "All Saints" => new[] { "All Saints", "All Saints Day", "All Saints' Day" },
        "Reformation" => new[] { "Reformation", "Reformation Sunday" },
        _ => new[] { seasonName },
    };
}
