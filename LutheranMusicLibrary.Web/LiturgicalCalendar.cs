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
