using Microsoft.Data.Sqlite;

public class DatabaseService
{
    private readonly string _dbPath;

    public DatabaseService(string dbPath)
    {
        _dbPath = dbPath;
    }

    private SqliteConnection GetConnection()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private static string Safe(SqliteDataReader r, string col)
    {
        try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? "" : r.GetString(o); }
        catch { return ""; }
    }

    private static int SafeInt(SqliteDataReader r, string col)
    {
        try { var o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : r.GetInt32(o); }
        catch { return 0; }
    }

    // ── HYMN TEXTS ────────────────────────────────────────────────────────────

    public List<HymnSummary> SearchHymns(string query = "", string season = "", string meter = "")
    {
        var results = new List<HymnSummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.id, t.title, t.author, t.year, t.meter, t.first_line, t.language,
                   GROUP_CONCAT(DISTINCT h.code || ' ' || thn.number) as hymnal_refs
            FROM texts t
            LEFT JOIN text_hymnal_numbers thn ON t.id = thn.text_id
            LEFT JOIN hymnals h ON thn.hymnal_id = h.id
            LEFT JOIN seasons s ON s.entity_type = 'text' AND s.entity_id = t.id
            WHERE ($q = '' OR t.title LIKE $q OR t.author LIKE $q OR t.first_line LIKE $q)
            AND ($season = '' OR s.season LIKE $season)
            AND ($meter = '' OR t.meter LIKE $meter)
            GROUP BY t.id
            ORDER BY t.title
            LIMIT 500";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        cmd.Parameters.AddWithValue("$season", string.IsNullOrWhiteSpace(season) ? "" : $"%{season}%");
        cmd.Parameters.AddWithValue("$meter", string.IsNullOrWhiteSpace(meter) ? "" : $"%{meter}%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new HymnSummary
            {
                Id = r.GetInt32(0), Title = Safe(r, "title"), Author = Safe(r, "author"),
                Year = SafeInt(r, "year"), Meter = Safe(r, "meter"),
                FirstLine = Safe(r, "first_line"), Language = Safe(r, "language"),
                HymnalRefs = Safe(r, "hymnal_refs"),
                Slug = Slugify(Safe(r, "title")),
            });
        return results;
    }

    public HymnDetail? GetHymnBySlug(string slug)
    {
        // Find by matching slugified title
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title FROM texts ORDER BY title";
        using var r = cmd.ExecuteReader();
        int id = 0;
        while (r.Read())
        {
            if (Slugify(r.GetString(1)) == slug) { id = r.GetInt32(0); break; }
        }
        r.Close();
        if (id == 0) return null;
        return GetHymnById(id);
    }

    public HymnDetail? GetHymnById(int id)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM texts WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var d = new HymnDetail
        {
            Id = id, Title = Safe(r, "title"), OriginalTitle = Safe(r, "title_original"),
            FirstLine = Safe(r, "first_line"), Author = Safe(r, "author"),
            AuthorDates = Safe(r, "author_dates"), Year = SafeInt(r, "year"),
            Meter = Safe(r, "meter"), Origin = Safe(r, "origin"), Language = Safe(r, "language"),
            Translator = Safe(r, "translator"), TextHistory = Safe(r, "text_history"),
            TheologicalAnalysis = Safe(r, "theological_analysis"),
            LawGospel = Safe(r, "law_gospel"), BulletinNote = Safe(r, "bulletin_note"),
            PreachingHook = Safe(r, "preaching_hook"), CcliNumber = Safe(r, "ccli_number"),
            Slug = Slugify(Safe(r, "title")),
        };
        r.Close();

        // Hymnal numbers
        var hn = conn.CreateCommand();
        hn.CommandText = @"SELECT h.code, h.name, thn.number FROM text_hymnal_numbers thn
                           JOIN hymnals h ON thn.hymnal_id = h.id WHERE thn.text_id = $id ORDER BY h.code";
        hn.Parameters.AddWithValue("$id", id);
        using var hnr = hn.ExecuteReader();
        while (hnr.Read()) d.HymnalNumbers.Add(new HymnalRef { Code = hnr.GetString(0), Name = hnr.GetString(1), Number = hnr.GetString(2) });

        // Tunes
        var tn = conn.CreateCommand();
        tn.CommandText = @"SELECT tu.id, tu.tune_name, tt.is_primary FROM text_tunes tt
                           JOIN tunes tu ON tt.tune_id = tu.id WHERE tt.text_id = $id ORDER BY tt.is_primary DESC";
        tn.Parameters.AddWithValue("$id", id);
        using var tnr = tn.ExecuteReader();
        while (tnr.Read()) d.Tunes.Add(new TuneRef { Id = tnr.GetInt32(0), TuneName = tnr.GetString(1), IsPrimary = tnr.GetInt32(2) == 1, Slug = Slugify(tnr.GetString(1)) });

        // Scripture refs
        var sc = conn.CreateCommand();
        sc.CommandText = "SELECT reference FROM scripture_refs WHERE entity_type='text' AND entity_id=$id";
        sc.Parameters.AddWithValue("$id", id);
        using var scr = sc.ExecuteReader();
        while (scr.Read()) d.ScriptureRefs.Add(scr.GetString(0));

        // Seasons
        var se = conn.CreateCommand();
        se.CommandText = "SELECT DISTINCT season FROM seasons WHERE entity_type='text' AND entity_id=$id";
        se.Parameters.AddWithValue("$id", id);
        using var ser = se.ExecuteReader();
        while (ser.Read()) d.Seasons.Add(ser.GetString(0));

        // Themes
        var th = conn.CreateCommand();
        th.CommandText = "SELECT DISTINCT theme FROM themes WHERE entity_type='text' AND entity_id=$id";
        th.Parameters.AddWithValue("$id", id);
        using var thr = th.ExecuteReader();
        while (thr.Read()) d.Themes.Add(thr.GetString(0));

        // PHH reference notes
        var no = conn.CreateCommand();
        no.CommandText = "SELECT note_type, content FROM reference_notes WHERE entity_type='text' AND entity_id=$id ORDER BY note_type";
        no.Parameters.AddWithValue("$id", id);
        using var nor = no.ExecuteReader();
        while (nor.Read()) d.ReferenceNotes.Add(new RefNote { NoteType = nor.GetString(0), Content = nor.GetString(1) });

        // Bach cantatas linked via tune
        if (d.Tunes.Count > 0)
        {
            var bc = conn.CreateCommand();
            bc.CommandText = @"SELECT bwv, title_english, year_composed, notes FROM bach_cantatas
                               WHERE tune_id IN (SELECT tune_id FROM text_tunes WHERE text_id = $id) LIMIT 5";
            bc.Parameters.AddWithValue("$id", id);
            using var bcr = bc.ExecuteReader();
            while (bcr.Read()) d.BachCantatas.Add($"{bcr.GetString(0)}: {(bcr.IsDBNull(1) ? "" : bcr.GetString(1))}{(bcr.IsDBNull(2) || bcr.GetInt32(2) == 0 ? "" : " (" + bcr.GetInt32(2) + ")")}");
        }

        return d;
    }

    // ── TUNES ─────────────────────────────────────────────────────────────────

    public List<TuneSummary> SearchTunes(string query = "")
    {
        var results = new List<TuneSummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT t.id, t.tune_name, t.meter, t.origin, t.tradition, t.standard_key,
                             COUNT(DISTINCT tt.text_id) as text_count
                             FROM tunes t LEFT JOIN text_tunes tt ON t.id = tt.tune_id
                             WHERE ($q = '' OR t.tune_name LIKE $q OR t.meter LIKE $q OR t.origin LIKE $q)
                             GROUP BY t.id ORDER BY t.tune_name LIMIT 500";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new TuneSummary
            {
                Id = r.GetInt32(0), TuneName = Safe(r, "tune_name"), Meter = Safe(r, "meter"),
                Origin = Safe(r, "origin"), Tradition = Safe(r, "tradition"),
                StandardKey = Safe(r, "standard_key"), TextCount = r.GetInt32(6),
                Slug = Slugify(Safe(r, "tune_name")),
            });
        return results;
    }

    public TuneDetail? GetTuneBySlug(string slug)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, tune_name FROM tunes ORDER BY tune_name";
        using var r = cmd.ExecuteReader();
        int id = 0;
        while (r.Read()) { if (Slugify(r.GetString(1)) == slug) { id = r.GetInt32(0); break; } }
        r.Close();
        if (id == 0) return null;

        var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT * FROM tunes WHERE id = $id";
        cmd2.Parameters.AddWithValue("$id", id);
        using var r2 = cmd2.ExecuteReader();
        if (!r2.Read()) return null;

        var d = new TuneDetail
        {
            Id = id, TuneName = Safe(r2, "tune_name"), Meter = Safe(r2, "meter"),
            StandardKey = Safe(r2, "standard_key"), AltKey = Safe(r2, "alt_key"),
            Origin = Safe(r2, "origin"), Tradition = Safe(r2, "tradition"),
            Notes = Safe(r2, "notes"), Slug = Slugify(Safe(r2, "tune_name")),
        };
        r2.Close();

        // Linked texts
        var tn = conn.CreateCommand();
        tn.CommandText = @"SELECT t.id, t.title, t.author, t.year, tt.is_primary FROM text_tunes tt
                           JOIN texts t ON tt.text_id = t.id WHERE tt.tune_id = $id ORDER BY tt.is_primary DESC, t.title";
        tn.Parameters.AddWithValue("$id", id);
        using var tnr = tn.ExecuteReader();
        while (tnr.Read())
            d.LinkedTexts.Add(new HymnSummary { Id = tnr.GetInt32(0), Title = tnr.GetString(1), Author = tnr.IsDBNull(2) ? "" : tnr.GetString(2), Year = tnr.IsDBNull(3) ? 0 : tnr.GetInt32(3), Slug = Slugify(tnr.GetString(1)) });

        // Bach cantatas
        var bc = conn.CreateCommand();
        bc.CommandText = "SELECT bwv, title_english, year_composed, scoring, notes FROM bach_cantatas WHERE tune_id = $id";
        bc.Parameters.AddWithValue("$id", id);
        using var bcr = bc.ExecuteReader();
        while (bcr.Read())
            d.BachCantatas.Add(new BachEntry { Bwv = bcr.GetString(0), TitleEnglish = bcr.IsDBNull(1) ? "" : bcr.GetString(1), YearComposed = bcr.IsDBNull(2) ? 0 : bcr.GetInt32(2), Scoring = bcr.IsDBNull(3) ? "" : bcr.GetString(3), Notes = bcr.IsDBNull(4) ? "" : bcr.GetString(4) });

        return d;
    }

    // ── PEOPLE ────────────────────────────────────────────────────────────────

    public List<PersonSummary> SearchPeople(string query = "")
    {
        var results = new List<PersonSummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, name, birth_year, death_year, country, biography FROM people
                             WHERE ($q = '' OR name LIKE $q OR country LIKE $q)
                             ORDER BY sort_name LIMIT 500";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new PersonSummary
            {
                Id = r.GetInt32(0), Name = Safe(r, "name"), BirthYear = SafeInt(r, "birth_year"),
                DeathYear = SafeInt(r, "death_year"), Country = Safe(r, "country"),
                HasBio = !string.IsNullOrEmpty(Safe(r, "biography")),
                Slug = Slugify(Safe(r, "name")),
            });
        return results;
    }

    public PersonDetail? GetPersonBySlug(string slug)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name FROM people ORDER BY sort_name";
        using var r = cmd.ExecuteReader();
        int id = 0;
        while (r.Read()) { if (Slugify(r.GetString(1)) == slug) { id = r.GetInt32(0); break; } }
        r.Close();
        if (id == 0) return null;

        var cmd2 = conn.CreateCommand();
        cmd2.CommandText = "SELECT * FROM people WHERE id = $id";
        cmd2.Parameters.AddWithValue("$id", id);
        using var r2 = cmd2.ExecuteReader();
        if (!r2.Read()) return null;

        var d = new PersonDetail
        {
            Id = id, Name = Safe(r2, "name"), BirthYear = SafeInt(r2, "birth_year"),
            DeathYear = SafeInt(r2, "death_year"), BirthPlace = Safe(r2, "birth_place"),
            DeathPlace = Safe(r2, "death_place"), Country = Safe(r2, "country"),
            Continent = Safe(r2, "continent"), Biography = Safe(r2, "biography"),
            Slug = Slugify(Safe(r2, "name")),
        };
        r2.Close();

        // Texts authored
        var ta = conn.CreateCommand();
        ta.CommandText = "SELECT id, title, year, meter FROM texts WHERE author LIKE $n ORDER BY year, title LIMIT 50";
        ta.Parameters.AddWithValue("$n", $"%{d.Name.Split(' ').Last()}%");
        using var tar = ta.ExecuteReader();
        while (tar.Read())
            d.TextsAuthored.Add(new HymnSummary { Id = tar.GetInt32(0), Title = tar.GetString(1), Year = tar.IsDBNull(2) ? 0 : tar.GetInt32(2), Meter = tar.IsDBNull(3) ? "" : tar.GetString(3), Slug = Slugify(tar.GetString(1)) });

        return d;
    }

    // ── STATS ─────────────────────────────────────────────────────────────────

    public SiteStats GetStats()
    {
        using var conn = GetConnection();
        int Count(string sql) { var c = conn.CreateCommand(); c.CommandText = sql; return Convert.ToInt32(c.ExecuteScalar()); }
        return new SiteStats
        {
            TextCount = Count("SELECT COUNT(*) FROM texts"),
            TuneCount = Count("SELECT COUNT(*) FROM tunes"),
            PeopleCount = Count("SELECT COUNT(*) FROM people"),
            HymnalCount = Count("SELECT COUNT(*) FROM hymnals"),
            ScriptureRefCount = Count("SELECT COUNT(*) FROM scripture_refs"),
        };
    }

    // ── SLUG HELPER ───────────────────────────────────────────────────────────

    public static string Slugify(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var slug = text.ToLowerInvariant();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[àáâãäå]", "a");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[èéêë]", "e");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[ìíîï]", "i");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[òóôõöø]", "o");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[ùúûü]", "u");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[ýÿ]", "y");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[ñ]", "n");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[ß]", "ss");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-+", "-");
        return slug.Trim('-');
    }
}

// ── MODELS ────────────────────────────────────────────────────────────────────

public class HymnSummary
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public int Year { get; set; }
    public string Meter { get; set; } = "";
    public string FirstLine { get; set; } = "";
    public string Language { get; set; } = "";
    public string HymnalRefs { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Dates => Year > 0 ? Year.ToString() : "";
}

public class HymnDetail
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string OriginalTitle { get; set; } = "";
    public string FirstLine { get; set; } = "";
    public string Author { get; set; } = "";
    public string AuthorDates { get; set; } = "";
    public int Year { get; set; }
    public string Meter { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Language { get; set; } = "";
    public string Translator { get; set; } = "";
    public string TextHistory { get; set; } = "";
    public string TheologicalAnalysis { get; set; } = "";
    public string LawGospel { get; set; } = "";
    public string BulletinNote { get; set; } = "";
    public string PreachingHook { get; set; } = "";
    public string CcliNumber { get; set; } = "";
    public string Slug { get; set; } = "";
    public string AuthorSlug => DatabaseService.Slugify(Author);
    public List<HymnalRef> HymnalNumbers { get; set; } = new();
    public List<TuneRef> Tunes { get; set; } = new();
    public List<string> ScriptureRefs { get; set; } = new();
    public List<string> Seasons { get; set; } = new();
    public List<string> Themes { get; set; } = new();
    public List<RefNote> ReferenceNotes { get; set; } = new();
    public List<string> BachCantatas { get; set; } = new();
}

public class HymnalRef { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string Number { get; set; } = ""; }
public class TuneRef { public int Id { get; set; } public string TuneName { get; set; } = ""; public bool IsPrimary { get; set; } public string Slug { get; set; } = ""; }
public class RefNote { public string NoteType { get; set; } = ""; public string Content { get; set; } = ""; }

public class TuneSummary
{
    public int Id { get; set; }
    public string TuneName { get; set; } = "";
    public string Meter { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Tradition { get; set; } = "";
    public string StandardKey { get; set; } = "";
    public int TextCount { get; set; }
    public string Slug { get; set; } = "";
}

public class TuneDetail
{
    public int Id { get; set; }
    public string TuneName { get; set; } = "";
    public string Meter { get; set; } = "";
    public string StandardKey { get; set; } = "";
    public string AltKey { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Tradition { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Slug { get; set; } = "";
    public List<HymnSummary> LinkedTexts { get; set; } = new();
    public List<BachEntry> BachCantatas { get; set; } = new();
}

public class BachEntry { public string Bwv { get; set; } = ""; public string TitleEnglish { get; set; } = ""; public int YearComposed { get; set; } public string Scoring { get; set; } = ""; public string Notes { get; set; } = ""; }

public class PersonSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int BirthYear { get; set; }
    public int DeathYear { get; set; }
    public string Country { get; set; } = "";
    public bool HasBio { get; set; }
    public string Slug { get; set; } = "";
    public string Dates => BirthYear > 0 ? $"{BirthYear}–{(DeathYear > 0 ? DeathYear.ToString() : "")}" : "";
}

public class PersonDetail
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int BirthYear { get; set; }
    public int DeathYear { get; set; }
    public string BirthPlace { get; set; } = "";
    public string DeathPlace { get; set; } = "";
    public string Country { get; set; } = "";
    public string Continent { get; set; } = "";
    public string Biography { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Dates => BirthYear > 0 ? $"{BirthYear}–{(DeathYear > 0 ? DeathYear.ToString() : "")}" : "";
    public List<HymnSummary> TextsAuthored { get; set; } = new();
}

public class SiteStats { public int TextCount { get; set; } public int TuneCount { get; set; } public int PeopleCount { get; set; } public int HymnalCount { get; set; } public int ScriptureRefCount { get; set; } }
