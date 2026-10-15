using System.Text;

/// <summary>Builds search-friendly page titles and meta descriptions from existing data.</summary>
public static class Seo
{
    public const string BaseUrl = "https://hymndesk.com";
    public const string SiteName = "Hymn Desk";
    public const string DefaultDescription =
        "Hymn Desk is a free reference for Lutheran church musicians: hymn texts, hymn tunes, authors and composers, hymnal numbers, lectionary and more.";

    /// <summary>Picks the first candidate title that fits in ~62 characters (falls back to the shortest).</summary>
    public static string PickTitle(params string[] candidates)
    {
        foreach (var c in candidates)
            if (c.Length <= 62) return c;
        return candidates[^1];
    }

    /// <summary>Trims to a word boundary at or under max characters.</summary>
    public static string Trim(string s, int max = 155)
    {
        s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        if (s.Length <= max) return s;
        var cut = s.LastIndexOf(' ', max - 1);
        if (cut < max / 2) cut = max - 1;
        return s.Substring(0, cut).TrimEnd(' ', ',', ';', ':', '-', '.') + "…";
    }

    public static string HymnalList(IEnumerable<HymnalRef> refs, int take = 4)
    {
        var items = refs.Where(r => !string.IsNullOrWhiteSpace(r.Number))
                        .Select(r => $"{(string.IsNullOrWhiteSpace(r.Code) ? r.Name : r.Code)} {r.Number}")
                        .Take(take).ToList();
        return string.Join(", ", items);
    }

    public static bool HasForeignTitle(HymnDetail h) =>
        !string.IsNullOrWhiteSpace(h.OriginalTitle) &&
        !string.Equals(h.OriginalTitle.Trim(), h.Title.Trim(), StringComparison.OrdinalIgnoreCase);

    public static string HymnTitle(HymnDetail h)
    {
        var withFirst = !string.IsNullOrWhiteSpace(h.FirstLine) &&
                        !string.Equals(h.FirstLine.Trim(), h.Title.Trim(), StringComparison.OrdinalIgnoreCase);
        var tail = HasForeignTitle(h) ? "Meaning, Translation, Hymnal Numbers" : "Hymn Text, History, Hymnal Numbers";
        var list = new List<string>();
        if (withFirst) list.Add($"{h.Title} ({h.FirstLine}) – {tail} | {SiteName}");
        list.Add($"{h.Title} – {tail} | {SiteName}");
        list.Add($"{h.Title} – Hymn Text & Hymnal Numbers | {SiteName}");
        list.Add($"{h.Title} | {SiteName}");
        return PickTitle(list.ToArray());
    }

    /// <summary>One plain-language sentence summarising the hymn, used as both the on-page summary and the description source.</summary>
    public static string HymnSummary(HymnDetail h)
    {
        var sb = new StringBuilder();
        sb.Append(h.Title);
        if (HasForeignTitle(h)) sb.Append($" (original title: “{h.OriginalTitle}”)");
        sb.Append(" is a hymn text");
        if (!string.IsNullOrWhiteSpace(h.Author)) sb.Append($" by {h.Author}");
        if (h.Year > 0) sb.Append($" ({h.Year})");
        if (!string.IsNullOrWhiteSpace(h.Meter)) sb.Append($" in {h.Meter} meter");
        sb.Append('.');
        if (!string.IsNullOrWhiteSpace(h.Translator)) sb.Append($" Translated by {h.Translator}.");
        var hymnals = HymnalList(h.HymnalNumbers);
        if (hymnals.Length > 0) sb.Append($" Found in {hymnals}.");
        var tune = h.Tunes.OrderByDescending(t => t.IsPrimary).FirstOrDefault();
        if (tune != null) sb.Append($" Sung to {tune.TuneName}.");
        return sb.ToString();
    }

    public static string HymnDescription(HymnDetail h) => Trim(HymnSummary(h) + " Read its history and meaning.");

    public static string TuneTitle(TuneDetail t)
    {
        var name = string.IsNullOrWhiteSpace(t.Meter) ? t.TuneName : $"{t.TuneName} ({t.Meter})";
        return PickTitle(
            $"{name} – Hymn Tune, Origin, Texts | {SiteName}",
            $"{name} – Hymn Tune | {SiteName}",
            $"{t.TuneName} – Hymn Tune | {SiteName}");
    }

    public static string TuneDescription(TuneDetail t)
    {
        var sb = new StringBuilder($"{t.TuneName} is a hymn tune");
        if (!string.IsNullOrWhiteSpace(t.Meter)) sb.Append($" in {t.Meter} meter");
        if (!string.IsNullOrWhiteSpace(t.Origin)) sb.Append($" ({t.Origin})");
        if (!string.IsNullOrWhiteSpace(t.StandardKey)) sb.Append($", usually set in {t.StandardKey}");
        sb.Append('.');
        if (t.LinkedTexts.Count > 0)
            sb.Append($" Hymn texts sung to it include {string.Join(", ", t.LinkedTexts.Take(3).Select(x => x.Title))}.");
        var hymnals = HymnalList(t.HymnalNumbers, 3);
        if (hymnals.Length > 0) sb.Append($" Hymnals: {hymnals}.");
        return Trim(sb.ToString());
    }

    public static string PersonTitle(PersonDetail p)
    {
        var role = p.Roles.Contains("composer") && !p.Roles.Contains("author") ? "Composer"
                 : p.Roles.Contains("author") && !p.Roles.Contains("composer") ? "Hymn Writer"
                 : "Hymn Writer & Composer";
        var dates = string.IsNullOrEmpty(p.Dates) ? "" : $" ({p.Dates})";
        return PickTitle(
            $"{p.Name}{dates} – {role}, Hymns | {SiteName}",
            $"{p.Name} – {role} | {SiteName}",
            $"{p.Name} | {SiteName}");
    }

    public static string PersonDescription(PersonDetail p)
    {
        var sb = new StringBuilder(p.Name);
        if (!string.IsNullOrEmpty(p.Dates)) sb.Append($" ({p.Dates})");
        sb.Append(p.Roles.Count > 0 ? $" was a hymn {string.Join("/", p.Roles)}" : " is part of the hymn tradition");
        if (!string.IsNullOrWhiteSpace(p.Country)) sb.Append($" from {p.Country}");
        sb.Append('.');
        if (p.TextsAuthored.Count > 0)
            sb.Append($" Hymns include {string.Join(", ", p.TextsAuthored.Take(3).Select(x => x.Title))}.");
        return Trim(sb.ToString());
    }
}
