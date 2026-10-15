using Microsoft.Data.Sqlite;

// Hymn devotions: short original devotions, each optionally linked to one hymn
// text through devotions.text_id (a hymn's id in the texts table). The table is
// created automatically at startup, so no manual SQL is needed. A devotion only
// appears on the site when its "published" column is 1.

public class DevotionHymn
{
    public int TextId { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class DevotionSummary
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Scripture { get; set; } = "";
    public string Season { get; set; } = "";
    public string Excerpt { get; set; } = "";
    public List<DevotionHymn> Hymns { get; set; } = new();
}

public class DevotionDetail : DevotionSummary
{
    public string Body { get; set; } = "";
}

public partial class DatabaseService
{
    private void EnsureDevotionTables()
    {
        try
        {
            using var conn = GetConnection();

            // Never create tables inside an empty or wrong database file; that
            // would only hide the real problem.
            var check = conn.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='texts'";
            if (Convert.ToInt32(check.ExecuteScalar()) == 0) return;

            var create = conn.CreateCommand();
            create.CommandText = @"
                CREATE TABLE IF NOT EXISTS devotions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    text_id INTEGER,
                    title TEXT NOT NULL,
                    scripture TEXT,
                    season TEXT,
                    body TEXT NOT NULL DEFAULT '',
                    published INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT DEFAULT CURRENT_TIMESTAMP
                );";
            create.ExecuteNonQuery();

            // An earlier version of the table had no text_id column. Add it.
            bool hasTextId = false;
            var info = conn.CreateCommand();
            info.CommandText = "PRAGMA table_info(devotions)";
            using (var r = info.ExecuteReader())
                while (r.Read()) if (r.GetString(1) == "text_id") hasTextId = true;
            if (!hasTextId)
            {
                var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE devotions ADD COLUMN text_id INTEGER";
                alter.ExecuteNonQuery();
            }

            // The earlier version also had a separate link table. Remove it, but
            // only if nothing was ever entered in it.
            var linkExists = conn.CreateCommand();
            linkExists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='text_devotions'";
            if (Convert.ToInt32(linkExists.ExecuteScalar()) > 0)
            {
                var linkCount = conn.CreateCommand();
                linkCount.CommandText = "SELECT COUNT(*) FROM text_devotions";
                if (Convert.ToInt32(linkCount.ExecuteScalar()) == 0)
                {
                    var drop = conn.CreateCommand();
                    drop.CommandText = "DROP TABLE text_devotions";
                    drop.ExecuteNonQuery();
                }
            }
        }
        catch
        {
            // Startup must never fail because of an optional feature.
        }
    }

    private static string MakeExcerpt(string body, int max = 220)
    {
        var flat = System.Text.RegularExpressions.Regex.Replace(body ?? "", @"\s+", " ").Trim();
        if (flat.Length <= max) return flat;
        var cut = flat.Substring(0, max);
        var space = cut.LastIndexOf(' ');
        return (space > 120 ? cut.Substring(0, space) : cut) + "…";
    }

    private const string DevotionSelect = @"
        SELECT d.id AS id, d.title AS title, d.scripture AS scripture, d.season AS season, d.body AS body,
               t.id AS hymn_id, t.title AS hymn_title
        FROM devotions d
        LEFT JOIN texts t ON t.id = d.text_id";

    private static DevotionDetail ReadDevotion(SqliteDataReader r)
    {
        var title = Safe(r, "title");
        var body = Safe(r, "body");
        var d = new DevotionDetail
        {
            Id = SafeInt(r, "id"),
            Title = title,
            Slug = Slugify(title),
            Scripture = Safe(r, "scripture"),
            Season = Safe(r, "season"),
            Body = body,
            Excerpt = MakeExcerpt(body),
        };
        var hymnTitle = Safe(r, "hymn_title");
        if (hymnTitle != "")
            d.Hymns.Add(new DevotionHymn { TextId = SafeInt(r, "hymn_id"), Title = hymnTitle, Slug = Slugify(hymnTitle) });
        return d;
    }

    public List<DevotionSummary> GetDevotions()
    {
        var list = new List<DevotionSummary>();
        try
        {
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = DevotionSelect + " WHERE d.published = 1 ORDER BY d.title";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadDevotion(r));
        }
        catch (SqliteException) { }
        return list;
    }

    public DevotionDetail? GetDevotionBySlug(string slug)
    {
        try
        {
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = DevotionSelect + " WHERE d.published = 1 ORDER BY d.id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var d = ReadDevotion(r);
                if (d.Slug == slug) return d;
            }
        }
        catch (SqliteException) { }
        return null;
    }

    public List<DevotionSummary> GetDevotionsForHymn(int textId)
    {
        var list = new List<DevotionSummary>();
        try
        {
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = DevotionSelect + " WHERE d.text_id = $id AND d.published = 1 ORDER BY d.title";
            cmd.Parameters.AddWithValue("$id", textId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadDevotion(r));
        }
        catch (SqliteException) { }
        return list;
    }

    public List<string> GetDevotionSitemapUrls()
    {
        var urls = new List<string>();
        try
        {
            using var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT title FROM devotions WHERE published = 1";
            using var r = cmd.ExecuteReader();
            while (r.Read()) urls.Add($"/devotions/{Slugify(r.GetString(0))}");
        }
        catch (SqliteException) { }
        return urls;
    }
}
