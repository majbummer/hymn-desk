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

    public PagedResult<HymnSummary> SearchHymns(string query, string season, string meter, string sort, int page, int pageSize)
    {
        var result = new PagedResult<HymnSummary> { Page = Math.Max(1, page), PageSize = pageSize };
        using var conn = GetConnection();

        var countCmd = conn.CreateCommand();
        countCmd.CommandText = @"
            SELECT COUNT(DISTINCT t.id)
            FROM texts t
            LEFT JOIN seasons s ON s.entity_type = 'text' AND s.entity_id = t.id
            WHERE ($q = '' OR t.title LIKE $q OR t.author LIKE $q OR t.first_line LIKE $q)
            AND ($season = '' OR s.season LIKE $season)
            AND ($meter = '' OR t.meter LIKE $meter)";
        countCmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        countCmd.Parameters.AddWithValue("$season", string.IsNullOrWhiteSpace(season) ? "" : $"%{season}%");
        countCmd.Parameters.AddWithValue("$meter", string.IsNullOrWhiteSpace(meter) ? "" : $"%{meter}%");
        result.TotalCount = Convert.ToInt32(countCmd.ExecuteScalar());

        var orderBy = sort == "year" ? "CASE WHEN t.year IS NULL OR t.year = 0 THEN 1 ELSE 0 END, t.year, t.title" : "t.title";
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
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
            ORDER BY {orderBy}
            LIMIT $take OFFSET $skip";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        cmd.Parameters.AddWithValue("$season", string.IsNullOrWhiteSpace(season) ? "" : $"%{season}%");
        cmd.Parameters.AddWithValue("$meter", string.IsNullOrWhiteSpace(meter) ? "" : $"%{meter}%");
        cmd.Parameters.AddWithValue("$take", pageSize);
        cmd.Parameters.AddWithValue("$skip", (result.Page - 1) * pageSize);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Items.Add(new HymnSummary
            {
                Id = r.GetInt32(0), Title = Safe(r, "title"), Author = Safe(r, "author"),
                Year = SafeInt(r, "year"), Meter = Safe(r, "meter"),
                FirstLine = Safe(r, "first_line"), Language = Safe(r, "language"),
                HymnalRefs = Safe(r, "hymnal_refs"),
                Slug = Slugify(Safe(r, "title")),
            });
        return result;
    }

    public string? GetRandomHymnSlug()
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT title FROM texts ORDER BY RANDOM() LIMIT 1";
        var title = cmd.ExecuteScalar() as string;
        return title == null ? null : Slugify(title);
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

    public PagedResult<TuneSummary> SearchTunes(string query, string sort, int page, int pageSize)
    {
        var result = new PagedResult<TuneSummary> { Page = Math.Max(1, page), PageSize = pageSize };
        using var conn = GetConnection();

        var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM tunes t WHERE ($q = '' OR t.tune_name LIKE $q OR t.meter LIKE $q OR t.origin LIKE $q)";
        countCmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        result.TotalCount = Convert.ToInt32(countCmd.ExecuteScalar());

        var orderBy = sort switch
        {
            "meter" => "t.meter, t.tune_name",
            "key" => "t.standard_key, t.tune_name",
            "origin" => "t.origin, t.tune_name",
            "tradition" => "t.tradition, t.tune_name",
            "count" => "text_count DESC, t.tune_name",
            _ => "t.tune_name",
        };
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT t.id, t.tune_name, t.meter, t.origin, t.tradition, t.standard_key,
                             COUNT(DISTINCT tt.text_id) as text_count
                             FROM tunes t LEFT JOIN text_tunes tt ON t.id = tt.tune_id
                             WHERE ($q = '' OR t.tune_name LIKE $q OR t.meter LIKE $q OR t.origin LIKE $q)
                             GROUP BY t.id ORDER BY {orderBy} LIMIT $take OFFSET $skip";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        cmd.Parameters.AddWithValue("$take", pageSize);
        cmd.Parameters.AddWithValue("$skip", (result.Page - 1) * pageSize);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result.Items.Add(new TuneSummary
            {
                Id = r.GetInt32(0), TuneName = Safe(r, "tune_name"), Meter = Safe(r, "meter"),
                Origin = Safe(r, "origin"), Tradition = Safe(r, "tradition"),
                StandardKey = Safe(r, "standard_key"), TextCount = r.GetInt32(6),
                Slug = Slugify(Safe(r, "tune_name")),
            });
        return result;
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

        // Bach cantatas — match by chorale tune name, since bach_cantatas.tune_id is populated for only 8 of 58 rows
        var bc = conn.CreateCommand();
        bc.CommandText = @"SELECT bwv, title, title_english, year_composed, scoring, sunday_occasion, lectionary_connection, notes
                            FROM bach_cantatas WHERE UPPER(chorale_tune) = UPPER($name)";
        bc.Parameters.AddWithValue("$name", d.TuneName);
        using var bcr = bc.ExecuteReader();
        while (bcr.Read())
            d.BachCantatas.Add(new BachEntry
            {
                Bwv = Safe(bcr, "bwv"), Title = Safe(bcr, "title"), TitleEnglish = Safe(bcr, "title_english"),
                YearComposed = SafeInt(bcr, "year_composed"), Scoring = Safe(bcr, "scoring"),
                SundayOccasion = Safe(bcr, "sunday_occasion"), LectionaryConnection = Safe(bcr, "lectionary_connection"),
                Notes = Safe(bcr, "notes"),
            });

        // Hymnal numbers for the tune itself
        var hn = conn.CreateCommand();
        hn.CommandText = @"SELECT h.code, h.name, thn.number FROM tune_hymnal_numbers thn
                            JOIN hymnals h ON thn.hymnal_id = h.id WHERE thn.tune_id = $id ORDER BY h.name";
        hn.Parameters.AddWithValue("$id", id);
        using var hnr = hn.ExecuteReader();
        while (hnr.Read())
            d.HymnalNumbers.Add(new HymnalRef { Code = Safe(hnr, "code"), Name = Safe(hnr, "name"), Number = Safe(hnr, "number") });

        // Stanza arc
        var sa = conn.CreateCommand();
        sa.CommandText = "SELECT arc FROM stanza_analysis WHERE tune_id = $id LIMIT 1";
        sa.Parameters.AddWithValue("$id", id);
        d.Arc = sa.ExecuteScalar() as string ?? "";

        return d;
    }

    // ── PEOPLE ────────────────────────────────────────────────────────────────

    public PagedResult<PersonSummary> SearchPeople(string query, string role, string era, string sort, int page, int pageSize)
    {
        var result = new PagedResult<PersonSummary> { Page = Math.Max(1, page), PageSize = pageSize };
        using var conn = GetConnection();

        var eraCase = @"
            CASE
                WHEN p.birth_year IS NULL OR p.birth_year <= 0 THEN ''
                WHEN p.birth_year < 500 THEN 'Early Church'
                WHEN p.birth_year < 1500 THEN 'Medieval'
                WHEN p.birth_year < 1650 THEN 'Reformation'
                WHEN p.birth_year < 1750 THEN 'Baroque'
                WHEN p.birth_year < 1830 THEN 'Classical'
                WHEN p.birth_year < 1900 THEN 'Romantic'
                WHEN p.birth_year < 1950 THEN 'Modern'
                ELSE 'Contemporary'
            END";
        var roleCondition = @"
            ($role = '' OR
             ($role != 'translator' AND EXISTS (SELECT 1 FROM person_roles pr2 WHERE pr2.person_id = p.id AND pr2.role = $role)) OR
             ($role = 'translator' AND EXISTS (SELECT 1 FROM texts tx2 WHERE tx2.translator = p.name)))";

        var countCmd = conn.CreateCommand();
        countCmd.CommandText = $@"
            SELECT COUNT(*) FROM people p
            WHERE ($q = '' OR p.name LIKE $q OR p.country LIKE $q)
            AND ($era = '' OR ({eraCase}) = $era)
            AND {roleCondition}";
        countCmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        countCmd.Parameters.AddWithValue("$era", era ?? "");
        countCmd.Parameters.AddWithValue("$role", role ?? "");
        result.TotalCount = Convert.ToInt32(countCmd.ExecuteScalar());

        var orderBy = sort == "birth" ? "p.birth_year, p.sort_name" : "p.sort_name";
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT p.id, p.name, p.birth_year, p.death_year, p.country, p.biography,
                   GROUP_CONCAT(DISTINCT pr.role) as roles,
                   (SELECT COUNT(*) FROM texts tx WHERE tx.translator = p.name) as translator_count
            FROM people p
            LEFT JOIN person_roles pr ON pr.person_id = p.id
            WHERE ($q = '' OR p.name LIKE $q OR p.country LIKE $q)
            AND ($era = '' OR ({eraCase}) = $era)
            AND {roleCondition}
            GROUP BY p.id
            ORDER BY {orderBy}
            LIMIT $take OFFSET $skip";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        cmd.Parameters.AddWithValue("$era", era ?? "");
        cmd.Parameters.AddWithValue("$role", role ?? "");
        cmd.Parameters.AddWithValue("$take", pageSize);
        cmd.Parameters.AddWithValue("$skip", (result.Page - 1) * pageSize);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var roles = Safe(r, "roles");
            if (r.GetInt32(7) > 0) roles = string.IsNullOrEmpty(roles) ? "translator" : roles + ",translator";
            result.Items.Add(new PersonSummary
            {
                Id = r.GetInt32(0), Name = Safe(r, "name"), BirthYear = SafeInt(r, "birth_year"),
                DeathYear = SafeInt(r, "death_year"), Country = Safe(r, "country"),
                HasBio = !string.IsNullOrEmpty(Safe(r, "biography")),
                Roles = roles,
                Slug = Slugify(Safe(r, "name")),
            });
        }
        return result;
    }

    public List<CountryGroup> GetCountryIndex()
    {
        var results = new List<CountryGroup>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT continent, country, COUNT(*) as cnt
            FROM people
            WHERE country IS NOT NULL AND country != ''
            GROUP BY continent, country
            ORDER BY continent, cnt DESC, country";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new CountryGroup
            {
                Continent = string.IsNullOrEmpty(Safe(r, "continent")) ? "Unspecified" : Safe(r, "continent"),
                Country = Safe(r, "country"), Count = r.GetInt32(2),
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

        // Roles (author/composer, from person_roles)
        var rc = conn.CreateCommand();
        rc.CommandText = "SELECT DISTINCT role FROM person_roles WHERE person_id = $id";
        rc.Parameters.AddWithValue("$id", id);
        using var rcr = rc.ExecuteReader();
        while (rcr.Read()) d.Roles.Add(rcr.GetString(0));

        // Tunes composed (via person_roles, entity_type='tune')
        var tc = conn.CreateCommand();
        tc.CommandText = @"SELECT t.tune_name FROM person_roles pr
                            JOIN tunes t ON pr.entity_id = t.id
                            WHERE pr.person_id = $id AND pr.role = 'composer' AND pr.entity_type = 'tune'
                            ORDER BY t.tune_name";
        tc.Parameters.AddWithValue("$id", id);
        using var tcr = tc.ExecuteReader();
        while (tcr.Read())
            d.TunesComposed.Add(new TuneStub { TuneName = tcr.GetString(0), Slug = Slugify(tcr.GetString(0)) });

        // Translations (exact match against texts.translator)
        if (!d.Roles.Contains("translator"))
        {
            var trCheck = conn.CreateCommand();
            trCheck.CommandText = "SELECT COUNT(*) FROM texts WHERE translator = $n";
            trCheck.Parameters.AddWithValue("$n", d.Name);
            if (Convert.ToInt32(trCheck.ExecuteScalar()) > 0) d.Roles.Add("translator");
        }
        var tr = conn.CreateCommand();
        tr.CommandText = "SELECT id, title, year, meter FROM texts WHERE translator = $n ORDER BY title";
        tr.Parameters.AddWithValue("$n", d.Name);
        using var trr = tr.ExecuteReader();
        while (trr.Read())
            d.Translations.Add(new HymnSummary { Id = trr.GetInt32(0), Title = trr.GetString(1), Year = trr.IsDBNull(2) ? 0 : trr.GetInt32(2), Meter = trr.IsDBNull(3) ? "" : trr.GetString(3), Slug = Slugify(trr.GetString(1)) });

        return d;
    }

    // ── HOMEPAGE / LITURGICAL SEASON ─────────────────────────────────────────────

    public SeasonInfo? GetSeasonInfo(string seasonName)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, color, character, description FROM church_year_seasons WHERE name = $name LIMIT 1";
        cmd.Parameters.AddWithValue("$name", seasonName);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new SeasonInfo
        {
            Name = Safe(r, "name"),
            Color = Safe(r, "color"),
            Character = Safe(r, "character"),
            Description = Safe(r, "description"),
        };
    }

    /// <summary>
    /// Picks one hymn text tagged for the given season, stable for a given seed
    /// (e.g. day-of-year) so the featured hymn changes daily but not on every
    /// page load. Tries each tag in order and falls back to any hymn if a
    /// season has no tagged texts yet.
    /// </summary>
    public HymnSummary? GetFeaturedHymnForSeason(string[] searchTags, int seed)
    {
        using var conn = GetConnection();

        foreach (var tag in searchTags)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT t.id, t.title, t.author, t.year, t.meter, t.first_line
                FROM texts t
                JOIN seasons s ON s.entity_type = 'text' AND s.entity_id = t.id
                WHERE s.season = $tag
                ORDER BY t.id";
            cmd.Parameters.AddWithValue("$tag", tag);
            using var r = cmd.ExecuteReader();
            var ids = new List<(int Id, string Title, string Author, int Year, string Meter, string FirstLine)>();
            while (r.Read())
                ids.Add((r.GetInt32(0), Safe(r, "title"), Safe(r, "author"), SafeInt(r, "year"), Safe(r, "meter"), Safe(r, "first_line")));
            if (ids.Count == 0) continue;

            var pick = ids[((seed % ids.Count) + ids.Count) % ids.Count];
            return new HymnSummary
            {
                Id = pick.Id, Title = pick.Title, Author = pick.Author,
                Year = pick.Year, Meter = pick.Meter, FirstLine = pick.FirstLine,
                Slug = Slugify(pick.Title),
            };
        }
        return null;
    }

    // ── METER INDEX ───────────────────────────────────────────────────────────

    public List<MeterGroup> GetMeterIndex()
    {
        var groups = new Dictionary<string, MeterGroup>();
        using var conn = GetConnection();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT meter, COUNT(*) as cnt FROM texts
                             WHERE meter IS NOT NULL AND meter != ''
                             GROUP BY meter";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var meter = r.GetString(0);
            groups[meter] = new MeterGroup { Meter = meter, HymnCount = r.GetInt32(1) };
        }

        var tn = conn.CreateCommand();
        tn.CommandText = "SELECT meter, tune_name FROM tunes WHERE meter IS NOT NULL AND meter != '' ORDER BY tune_name";
        using var tr = tn.ExecuteReader();
        while (tr.Read())
        {
            var meter = tr.GetString(0);
            var tuneName = tr.GetString(1);
            if (!groups.ContainsKey(meter)) groups[meter] = new MeterGroup { Meter = meter };
            groups[meter].Tunes.Add(new TuneStub { TuneName = tuneName, Slug = Slugify(tuneName) });
        }

        return groups.Values.OrderByDescending(g => g.HymnCount).ThenBy(g => g.Meter).ToList();
    }

    // ── FIRST LINE INDEX ─────────────────────────────────────────────────────

    public List<HymnSummary> GetFirstLineIndex(string query = "")
    {
        var results = new List<HymnSummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, title, author, year, meter, first_line FROM texts
                             WHERE first_line IS NOT NULL AND first_line != ''
                             AND ($q = '' OR first_line LIKE $q OR title LIKE $q)
                             ORDER BY first_line COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new HymnSummary
            {
                Id = r.GetInt32(0), Title = Safe(r, "title"), Author = Safe(r, "author"),
                Year = SafeInt(r, "year"), Meter = Safe(r, "meter"), FirstLine = Safe(r, "first_line"),
                Slug = Slugify(Safe(r, "title")),
            });
        return results;
    }

    // ── GLOSSARY ──────────────────────────────────────────────────────────────

    public List<GlossaryEntry> GetGlossary()
    {
        var results = new List<GlossaryEntry>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT term, category, definition, related_terms, lutheran_context FROM glossary ORDER BY category, term";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new GlossaryEntry
            {
                Term = Safe(r, "term"), Category = Safe(r, "category"), Definition = Safe(r, "definition"),
                RelatedTerms = Safe(r, "related_terms"), LutheranContext = Safe(r, "lutheran_context"),
            });
        return results;
    }

    // ── BACH CANTATAS ─────────────────────────────────────────────────────────

    /// <summary>
    /// The order Bach's own occasions fall in across the church year, starting at Advent 1.
    /// Uses the historic Leipzig lectionary naming (Sundays after Trinity, not after Pentecost).
    /// Movable fixed feasts (Michael and All Angels, Reformation Day, etc.) are placed at their
    /// typical position relative to the numbered Trinity Sundays — exact placement shifts a week
    /// or two year to year since Trinity Sunday's date depends on Easter, but this gets the overall
    /// sequence right. Anything not in this list sorts to the end, in its original order.
    /// </summary>
    private static readonly string[] BachChurchYearOrder =
    {
        "First Sunday of Advent", "Second Sunday of Advent", "Third Sunday of Advent", "Fourth Sunday of Advent",
        "Christmas Day", "Third Day of Christmas", "Christmas through Epiphany",
        "Epiphany", "Second Sunday after Epiphany",
        "Purification / Presentation of Our Lord",
        "Septuagesima Sunday", "Sexagesima Sunday", "Quinquagesima", "Quinquagesima Sunday", "Quinquagesima (before Lent)",
        "Palm Sunday",
        "Easter Sunday", "Easter Monday", "First Sunday after Easter", "Third Sunday after Easter",
        "Ascension", "Pentecost", "Holy Trinity",
        "First Sunday after Trinity", "Second Sunday after Trinity",
        "St. John the Baptist / Baptism",
        "Third Sunday after Trinity", "Visitation of Mary",
        "Fifth Sunday after Trinity", "Sixth Sunday after Trinity",
        "Ninth Sunday after Trinity", "Eleventh Sunday after Trinity",
        "Fourteenth Sunday after Trinity", "Fifteenth Sunday after Trinity", "Sixteenth Sunday after Trinity",
        "Eighteenth Sunday after Trinity", "Nineteenth Sunday after Trinity",
        "Twenty-first Sunday after Trinity", "Twenty-second Sunday after Trinity",
        "Feast of St. Michael and All Angels",
        "Twenty-fourth Sunday after Trinity",
        "Reformation Day",
        "Second-to-last Sunday after Trinity", "Twenty-seventh Sunday after Trinity",
        "Funeral", "Funeral / Penitential",
    };

    public List<BachEntry> GetBachCantatas(string query = "")
    {
        var results = new List<BachEntry>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT bc.bwv, bc.title, bc.title_english, bc.sunday_occasion, bc.year_composed,
                   bc.scoring, bc.chorale_tune, bc.lectionary_connection, bc.notes, tu.tune_name
            FROM bach_cantatas bc
            LEFT JOIN tunes tu ON UPPER(tu.tune_name) = UPPER(bc.chorale_tune)
            WHERE ($q = '' OR bc.title LIKE $q OR bc.title_english LIKE $q OR bc.sunday_occasion LIKE $q OR bc.chorale_tune LIKE $q)
            GROUP BY bc.id
            ORDER BY bc.bwv";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query}%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var matchedTune = Safe(r, "tune_name");
            results.Add(new BachEntry
            {
                Bwv = Safe(r, "bwv"), Title = Safe(r, "title"), TitleEnglish = Safe(r, "title_english"),
                SundayOccasion = Safe(r, "sunday_occasion"), YearComposed = SafeInt(r, "year_composed"),
                Scoring = Safe(r, "scoring"), ChoraleTune = Safe(r, "chorale_tune"),
                LectionaryConnection = Safe(r, "lectionary_connection"), Notes = Safe(r, "notes"),
                TuneSlug = string.IsNullOrEmpty(matchedTune) ? "" : Slugify(matchedTune),
            });
        }

        var orderIndex = BachChurchYearOrder.Select((name, i) => new { name, i }).ToDictionary(x => x.name, x => x.i);
        return results
            .OrderBy(e => orderIndex.ContainsKey(e.SundayOccasion) ? orderIndex[e.SundayOccasion] : int.MaxValue)
            .ThenBy(e => e.SundayOccasion)
            .ThenBy(e => e.Bwv)
            .ToList();
    }

    // ── BIBLE LOOKUP ──────────────────────────────────────────────────────────

    public static readonly (string Code, string Label)[] BibleTranslations = new[]
    {
        ("kjv", "KJV — King James Version"),
        ("web", "WEB — World English Bible"),
        ("bsb", "BSB — Berean Standard Bible"),
        ("asv", "ASV — American Standard Version"),
        ("akjv", "AKJV — American King James"),
        ("cpdv", "CPDV — Catholic Public Domain Version"),
        ("dbt", "DBT — Darby Translation"),
        ("drb", "DRB — Douay-Rheims"),
        ("erv", "ERV — English Revised Version"),
        ("jps_wey", "JPS/Weymouth"),
        ("nheb", "NHEB — New Heart English Bible"),
        ("slt", "SLT — Smith's Literal Translation"),
        ("wbt", "WBT — Webster Bible Translation"),
        ("ylt", "YLT — Young's Literal Translation"),
    };

    public List<BibleBookSummary> GetBibleBooks()
    {
        var results = new List<BibleBookSummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT book_number, name, abbreviation, testament, chapter_count FROM bible_books ORDER BY book_number";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new BibleBookSummary
            {
                Number = r.GetInt32(0), Name = r.GetString(1), Abbreviation = r.GetString(2),
                Testament = r.GetString(3), ChapterCount = r.GetInt32(4),
            });
        return results;
    }

    public BibleChapterDetail? GetBibleChapter(string bookSlug, int chapter, string translationCode)
    {
        using var conn = GetConnection();

        var bookCmd = conn.CreateCommand();
        bookCmd.CommandText = "SELECT book_number, name, chapter_count FROM bible_books";
        using var br = bookCmd.ExecuteReader();
        int bookNumber = 0; string bookName = ""; int chapterCount = 0;
        while (br.Read())
        {
            if (Slugify(br.GetString(1)) == bookSlug) { bookNumber = br.GetInt32(0); bookName = br.GetString(1); chapterCount = br.GetInt32(2); break; }
        }
        br.Close();
        if (bookNumber == 0) return null;
        if (chapter < 1) chapter = 1;
        if (chapter > chapterCount) chapter = chapterCount;

        var validCodes = BibleTranslations.Select(t => t.Code).ToHashSet();
        if (!validCodes.Contains(translationCode)) translationCode = "kjv";
        var col = translationCode + "_text";

        var d = new BibleChapterDetail
        {
            BookName = bookName, BookSlug = bookSlug, BookNumber = bookNumber, Chapter = chapter, ChapterCount = chapterCount,
            TranslationCode = translationCode,
            TranslationLabel = BibleTranslations.First(t => t.Code == translationCode).Label,
        };

        var vcmd = conn.CreateCommand();
        vcmd.CommandText = $@"SELECT verse, {col}, lutheran_short FROM bible_verses
                              WHERE book_number = $bn AND chapter = $ch ORDER BY verse";
        vcmd.Parameters.AddWithValue("$bn", bookNumber);
        vcmd.Parameters.AddWithValue("$ch", chapter);
        using var vr = vcmd.ExecuteReader();
        while (vr.Read())
            d.Verses.Add(new BibleVerseText
            {
                Verse = vr.GetInt32(0),
                Text = vr.IsDBNull(1) ? "" : vr.GetString(1),
                LutheranShort = vr.IsDBNull(2) ? "" : vr.GetString(2),
            });

        var scmd = conn.CreateCommand();
        scmd.CommandText = @"SELECT lutheran_summary, major_theme, key_people, key_location, liturgical_use
                              FROM bible_chapter_summaries WHERE book_number = $bn AND chapter = $ch LIMIT 1";
        scmd.Parameters.AddWithValue("$bn", bookNumber);
        scmd.Parameters.AddWithValue("$ch", chapter);
        using var sr = scmd.ExecuteReader();
        if (sr.Read())
        {
            d.ChapterSummary = Safe(sr, "lutheran_summary");
            d.MajorTheme = Safe(sr, "major_theme");
            d.KeyPeople = Safe(sr, "key_people");
            d.KeyLocation = Safe(sr, "key_location");
            d.LiturgicalUse = Safe(sr, "liturgical_use");
        }
        sr.Close();

        var hcmd = conn.CreateCommand();
        hcmd.CommandText = @"
            SELECT DISTINCT t.id, t.title, t.author, t.year, t.meter, t.first_line
            FROM scripture_refs sr
            JOIN texts t ON sr.entity_type = 'text' AND sr.entity_id = t.id
            WHERE sr.reference LIKE $prefixColon OR sr.reference = $exact
            ORDER BY t.title";
        hcmd.Parameters.AddWithValue("$prefixColon", $"{bookName} {chapter}:%");
        hcmd.Parameters.AddWithValue("$exact", $"{bookName} {chapter}");
        using var hr = hcmd.ExecuteReader();
        while (hr.Read())
            d.RelatedHymns.Add(new HymnSummary
            {
                Id = hr.GetInt32(0), Title = Safe(hr, "title"), Author = Safe(hr, "author"),
                Year = SafeInt(hr, "year"), Meter = Safe(hr, "meter"), FirstLine = Safe(hr, "first_line"),
                Slug = Slugify(Safe(hr, "title")),
            });

        return d;
    }

    // ── CONFESSIONS & CATECHISMS ──────────────────────────────────────────────

    public List<ConfessionalDocumentSummary> GetConfessionalDocuments()
    {
        var results = new List<ConfessionalDocumentSummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT d.id, d.name, d.slug, d.year, d.description, COUNT(s.id) as section_count
            FROM confessional_documents d
            LEFT JOIN confessional_sections s ON s.document_id = d.id
            GROUP BY d.id
            ORDER BY d.sort_order";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new ConfessionalDocumentSummary
            {
                Id = r.GetInt32(0), Name = Safe(r, "name"), Slug = Safe(r, "slug"),
                Year = Safe(r, "year"), Description = Safe(r, "description"),
                SectionCount = r.GetInt32(5),
            });
        return results;
    }

    public ConfessionalDocumentDetail? GetConfessionalDocumentBySlug(string slug)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, slug, year, description FROM confessional_documents WHERE slug = $slug";
        cmd.Parameters.AddWithValue("$slug", slug);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var d = new ConfessionalDocumentDetail
        {
            Id = r.GetInt32(0), Name = Safe(r, "name"), Slug = Safe(r, "slug"),
            Year = Safe(r, "year"), Description = Safe(r, "description"),
        };
        r.Close();

        var sc = conn.CreateCommand();
        sc.CommandText = @"SELECT id, part, number, title FROM confessional_sections
                            WHERE document_id = $id ORDER BY sort_order";
        sc.Parameters.AddWithValue("$id", d.Id);
        using var scr = sc.ExecuteReader();
        while (scr.Read())
            d.Sections.Add(new ConfessionalSectionStub
            {
                Id = scr.GetInt32(0), Part = Safe(scr, "part"), Number = Safe(scr, "number"), Title = Safe(scr, "title"),
            });
        return d;
    }

    public ConfessionalSectionDetail? GetConfessionalSection(int sectionId)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id, s.part, s.number, s.title, s.content, d.id, d.name, d.slug
            FROM confessional_sections s
            JOIN confessional_documents d ON s.document_id = d.id
            WHERE s.id = $id";
        cmd.Parameters.AddWithValue("$id", sectionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var d = new ConfessionalSectionDetail
        {
            Id = r.GetInt32(0), Part = Safe(r, "part"), Number = Safe(r, "number"), Title = Safe(r, "title"),
            Content = Safe(r, "content"), DocumentId = r.GetInt32(5), DocumentName = Safe(r, "name"), DocumentSlug = Safe(r, "slug"),
        };
        r.Close();

        var hc = conn.CreateCommand();
        hc.CommandText = @"
            SELECT t.id, t.title, t.author, t.year, t.meter, t.first_line
            FROM hymn_confessional_links hcl
            JOIN texts t ON hcl.text_id = t.id
            WHERE hcl.section_id = $id
            ORDER BY t.title";
        hc.Parameters.AddWithValue("$id", sectionId);
        using var hr = hc.ExecuteReader();
        while (hr.Read())
            d.RelatedHymns.Add(new HymnSummary
            {
                Id = hr.GetInt32(0), Title = Safe(hr, "title"), Author = Safe(hr, "author"),
                Year = SafeInt(hr, "year"), Meter = Safe(hr, "meter"), FirstLine = Safe(hr, "first_line"),
                Slug = Slugify(Safe(hr, "title")),
            });
        return d;
    }

    /// <summary>Confessional sections linked to a given hymn text, for display on the hymn detail page.</summary>
    public List<ConfessionalSectionStub> GetConfessionalSectionsForHymn(int textId)
    {
        var results = new List<ConfessionalSectionStub>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id, s.part, s.number, s.title, d.name, d.slug
            FROM hymn_confessional_links hcl
            JOIN confessional_sections s ON hcl.section_id = s.id
            JOIN confessional_documents d ON s.document_id = d.id
            WHERE hcl.text_id = $id
            ORDER BY d.sort_order, s.sort_order";
        cmd.Parameters.AddWithValue("$id", textId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new ConfessionalSectionStub
            {
                Id = r.GetInt32(0), Part = Safe(r, "part"), Number = Safe(r, "number"), Title = Safe(r, "title"),
                DocumentName = Safe(r, "name"), DocumentSlug = Safe(r, "slug"),
            });
        return results;
    }

    // ── LAW/GOSPEL INDEX ──────────────────────────────────────────────────────

    /// <summary>
    /// Groups hymns by whether their existing law_gospel analysis text mentions
    /// "Law" and/or "Gospel" — a simple, transparent heuristic, not a doctrinal
    /// judgment call. Hymns with no analysis or an "unable to determine" note
    /// are left out of the browsable index entirely.
    /// </summary>
    public Dictionary<string, List<HymnSummary>> GetLawGospelIndex()
    {
        var results = new Dictionary<string, List<HymnSummary>> { ["Law & Gospel"] = new(), ["Gospel"] = new(), ["Law"] = new() };
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, title, author, year, meter, first_line,
                   CASE
                       WHEN law_gospel LIKE '%unable to determine%' THEN 'Unclear'
                       WHEN law_gospel LIKE '%law%' AND law_gospel LIKE '%gospel%' THEN 'Law & Gospel'
                       WHEN law_gospel LIKE '%law%' THEN 'Law'
                       WHEN law_gospel LIKE '%gospel%' THEN 'Gospel'
                       ELSE 'Unclear'
                   END as category
            FROM texts
            WHERE law_gospel IS NOT NULL AND law_gospel != ''
            ORDER BY title";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var category = Safe(r, "category");
            if (!results.ContainsKey(category)) continue;
            results[category].Add(new HymnSummary
            {
                Id = r.GetInt32(0), Title = Safe(r, "title"), Author = Safe(r, "author"),
                Year = SafeInt(r, "year"), Meter = Safe(r, "meter"), FirstLine = Safe(r, "first_line"),
                Slug = Slugify(Safe(r, "title")),
            });
        }
        return results;
    }

    // ── LECTIONARY PLANNER ────────────────────────────────────────────────────

    public List<LectionarySundaySummary> GetLectionaryIndex(string yearCycle)
    {
        var results = new List<LectionarySundaySummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, sunday_code, sunday_name, season, color
                             FROM lectionary WHERE year_cycle = $yc ORDER BY id";
        cmd.Parameters.AddWithValue("$yc", yearCycle);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new LectionarySundaySummary
            {
                Id = r.GetInt32(0), SundayCode = Safe(r, "sunday_code"), SundayName = Safe(r, "sunday_name"),
                Season = Safe(r, "season"), Color = Safe(r, "color"),
            });
        return results;
    }

    public LectionaryDetail? GetLectionaryDetail(int id)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, year_cycle, sunday_code, sunday_name, season, color,
                   first_reading, first_reading_summary, psalm, psalm_summary,
                   second_reading, second_reading_summary, gospel, gospel_summary
            FROM lectionary WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var d = new LectionaryDetail
        {
            Id = r.GetInt32(0), YearCycle = Safe(r, "year_cycle"), SundayCode = Safe(r, "sunday_code"),
            SundayName = Safe(r, "sunday_name"), Season = Safe(r, "season"), Color = Safe(r, "color"),
            FirstReading = Safe(r, "first_reading"), FirstReadingSummary = Safe(r, "first_reading_summary"),
            Psalm = Safe(r, "psalm"), PsalmSummary = Safe(r, "psalm_summary"),
            SecondReading = Safe(r, "second_reading"), SecondReadingSummary = Safe(r, "second_reading_summary"),
            Gospel = Safe(r, "gospel"), GospelSummary = Safe(r, "gospel_summary"),
        };
        r.Close();

        var hc = conn.CreateCommand();
        hc.CommandText = @"
            SELECT lhs.match_type, lhs.matched_ref, lhs.source, lhs.slot,
                   t.id, t.title, t.author, t.year, t.meter, t.first_line
            FROM lectionary_hymn_suggestions lhs
            JOIN texts t ON lhs.text_id = t.id
            WHERE lhs.lectionary_id = $id
            ORDER BY lhs.source, lhs.slot, lhs.match_type, t.title";
        hc.Parameters.AddWithValue("$id", id);
        using var hr = hc.ExecuteReader();
        while (hr.Read())
        {
            var matchType = Safe(hr, "match_type");
            var source = Safe(hr, "source");
            var slot = Safe(hr, "slot");
            var hymn = new HymnSummary
            {
                Id = hr.GetInt32(4), Title = Safe(hr, "title"), Author = Safe(hr, "author"),
                Year = SafeInt(hr, "year"), Meter = Safe(hr, "meter"), FirstLine = Safe(hr, "first_line"),
                Slug = Slugify(Safe(hr, "title")),
                Source = source, Slot = slot, MatchType = matchType, MatchedRef = Safe(hr, "matched_ref"),
            };
            if (source == "auto")
            {
                if (!d.HymnsByMatchType.ContainsKey(matchType)) d.HymnsByMatchType[matchType] = new List<HymnSummary>();
                d.HymnsByMatchType[matchType].Add(hymn);
            }
            else
            {
                var slotKey = string.IsNullOrEmpty(slot) ? "hymn_suggestion" : slot;
                if (!d.SuggestionsBySourceAndSlot.ContainsKey(source))
                    d.SuggestionsBySourceAndSlot[source] = new Dictionary<string, List<HymnSummary>>();
                if (!d.SuggestionsBySourceAndSlot[source].ContainsKey(slotKey))
                    d.SuggestionsBySourceAndSlot[source][slotKey] = new List<HymnSummary>();
                d.SuggestionsBySourceAndSlot[source][slotKey].Add(hymn);
            }
        }
        return d;
    }

    // ── HYMN OF THE DAY CALENDAR ─────────────────────────────────────────────────

    public List<HymnOfTheDayEntry> GetHymnOfTheDayCalendar()
    {
        var results = new List<HymnOfTheDayEntry>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT hod.season, hod.sunday_name, hod.hymnal, hod.hymn_number, hod.hymn_title, hod.notes,
                   t.id, t.title
            FROM hymn_of_the_day hod
            LEFT JOIN texts t ON hod.text_id = t.id
            ORDER BY hod.id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new HymnOfTheDayEntry
            {
                Season = Safe(r, "season"), SundayName = Safe(r, "sunday_name"),
                Hymnal = Safe(r, "hymnal"), HymnNumber = Safe(r, "hymn_number"),
                HymnTitle = Safe(r, "hymn_title"), Notes = Safe(r, "notes"),
                TextSlug = r.IsDBNull(6) ? "" : Slugify(Safe(r, "title")),
            });
        return results;
    }

    // ── COMPARE HYMNALS ───────────────────────────────────────────────────────

    public List<HymnalInfo> GetHymnalsList()
    {
        var results = new List<HymnalInfo>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, code, name, year FROM hymnals ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new HymnalInfo { Id = r.GetInt32(0), Code = Safe(r, "code"), Name = Safe(r, "name"), Year = SafeInt(r, "year") });
        return results;
    }

    public List<HymnalComparisonRow> CompareHymnals(string code1, string code2)
    {
        var results = new List<HymnalComparisonRow>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.id, t.title, thn1.number as num1, thn2.number as num2
            FROM texts t
            JOIN text_hymnal_numbers thn1 ON thn1.text_id = t.id
            JOIN hymnals h1 ON thn1.hymnal_id = h1.id AND h1.code = $c1
            JOIN text_hymnal_numbers thn2 ON thn2.text_id = t.id
            JOIN hymnals h2 ON thn2.hymnal_id = h2.id AND h2.code = $c2
            ORDER BY CAST(thn1.number AS INTEGER), t.title";
        cmd.Parameters.AddWithValue("$c1", code1);
        cmd.Parameters.AddWithValue("$c2", code2);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new HymnalComparisonRow
            {
                TextId = r.GetInt32(0), Title = Safe(r, "title"),
                Number1 = Safe(r, "num1"), Number2 = Safe(r, "num2"),
                Slug = Slugify(Safe(r, "title")),
            });
        return results;
    }

    /// <summary>The narrative/theological arc for a hymn's primary tune, from stanza_analysis.</summary>
    public string? GetStanzaArcForHymn(int textId)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT sa.arc
            FROM text_tunes tt
            JOIN stanza_analysis sa ON sa.tune_id = tt.tune_id
            WHERE tt.text_id = $id
            ORDER BY tt.is_primary DESC
            LIMIT 1";
        cmd.Parameters.AddWithValue("$id", textId);
        return cmd.ExecuteScalar() as string;
    }

    // ── SERVICE BUILDER ───────────────────────────────────────────────────────

    public static readonly string[] ServiceSlots = { "Entrance", "Hymn of the Day", "Sermon Hymn", "Offertory", "Communion", "Sending" };

    public List<ServicePlanSummary> GetServicePlans()
    {
        var results = new List<ServicePlanSummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT sp.id, sp.plan_date, sp.note, COUNT(spi.id) as item_count
            FROM service_plans sp
            LEFT JOIN service_plan_items spi ON spi.plan_id = sp.id
            GROUP BY sp.id
            ORDER BY sp.plan_date DESC, sp.id DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new ServicePlanSummary
            {
                Id = r.GetInt32(0), PlanDate = Safe(r, "plan_date"), Note = Safe(r, "note"), ItemCount = r.GetInt32(3),
            });
        return results;
    }

    public int CreateServicePlan(string planDate, string note)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO service_plans (plan_date, note) VALUES ($date, $note); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$date", planDate ?? "");
        cmd.Parameters.AddWithValue("$note", note ?? "");
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void DeleteServicePlan(int planId)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM service_plan_items WHERE plan_id = $id; DELETE FROM service_plans WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", planId);
        cmd.ExecuteNonQuery();
    }

    public ServicePlanDetail? GetServicePlan(int planId)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, plan_date, note FROM service_plans WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", planId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var d = new ServicePlanDetail { Id = r.GetInt32(0), PlanDate = Safe(r, "plan_date"), Note = Safe(r, "note") };
        r.Close();

        foreach (var slot in ServiceSlots) d.ItemsBySlot[slot] = new List<ServicePlanItem>();

        var ic = conn.CreateCommand();
        ic.CommandText = @"
            SELECT spi.id, spi.slot, spi.sort_order, t.id, t.title, t.author, t.year, t.meter, t.first_line
            FROM service_plan_items spi
            JOIN texts t ON spi.text_id = t.id
            WHERE spi.plan_id = $id
            ORDER BY spi.slot, spi.sort_order";
        ic.Parameters.AddWithValue("$id", planId);
        using var ir = ic.ExecuteReader();
        while (ir.Read())
        {
            var slot = Safe(ir, "slot");
            if (!d.ItemsBySlot.ContainsKey(slot)) d.ItemsBySlot[slot] = new List<ServicePlanItem>();
            d.ItemsBySlot[slot].Add(new ServicePlanItem
            {
                ItemId = ir.GetInt32(0), Slot = slot, SortOrder = ir.GetInt32(2),
                Hymn = new HymnSummary
                {
                    Id = ir.GetInt32(3), Title = Safe(ir, "title"), Author = Safe(ir, "author"),
                    Year = SafeInt(ir, "year"), Meter = Safe(ir, "meter"), FirstLine = Safe(ir, "first_line"),
                    Slug = Slugify(Safe(ir, "title")),
                },
            });
        }
        return d;
    }

    public void AddServicePlanItem(int planId, string slot, int textId)
    {
        using var conn = GetConnection();
        var maxCmd = conn.CreateCommand();
        maxCmd.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM service_plan_items WHERE plan_id = $pid AND slot = $slot";
        maxCmd.Parameters.AddWithValue("$pid", planId);
        maxCmd.Parameters.AddWithValue("$slot", slot);
        int nextOrder = Convert.ToInt32(maxCmd.ExecuteScalar());

        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO service_plan_items (plan_id, slot, sort_order, text_id) VALUES ($pid, $slot, $order, $tid)";
        cmd.Parameters.AddWithValue("$pid", planId);
        cmd.Parameters.AddWithValue("$slot", slot);
        cmd.Parameters.AddWithValue("$order", nextOrder);
        cmd.Parameters.AddWithValue("$tid", textId);
        cmd.ExecuteNonQuery();
    }

    public void RemoveServicePlanItem(int itemId)
    {
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM service_plan_items WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", itemId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Swaps this item's sort_order with its neighbor in the same slot ("up" or "down").</summary>
    public void MoveServicePlanItem(int itemId, string direction)
    {
        using var conn = GetConnection();
        var cur = conn.CreateCommand();
        cur.CommandText = "SELECT plan_id, slot, sort_order FROM service_plan_items WHERE id = $id";
        cur.Parameters.AddWithValue("$id", itemId);
        using var r = cur.ExecuteReader();
        if (!r.Read()) return;
        int planId = r.GetInt32(0); string slot = r.GetString(1); int order = r.GetInt32(2);
        r.Close();

        var neighbor = conn.CreateCommand();
        neighbor.CommandText = direction == "up"
            ? "SELECT id, sort_order FROM service_plan_items WHERE plan_id = $pid AND slot = $slot AND sort_order < $order ORDER BY sort_order DESC LIMIT 1"
            : "SELECT id, sort_order FROM service_plan_items WHERE plan_id = $pid AND slot = $slot AND sort_order > $order ORDER BY sort_order ASC LIMIT 1";
        neighbor.Parameters.AddWithValue("$pid", planId);
        neighbor.Parameters.AddWithValue("$slot", slot);
        neighbor.Parameters.AddWithValue("$order", order);
        using var nr = neighbor.ExecuteReader();
        if (!nr.Read()) return;
        int neighborId = nr.GetInt32(0); int neighborOrder = nr.GetInt32(1);
        nr.Close();

        var upd1 = conn.CreateCommand();
        upd1.CommandText = "UPDATE service_plan_items SET sort_order = $o WHERE id = $id";
        upd1.Parameters.AddWithValue("$o", neighborOrder); upd1.Parameters.AddWithValue("$id", itemId);
        upd1.ExecuteNonQuery();

        var upd2 = conn.CreateCommand();
        upd2.CommandText = "UPDATE service_plan_items SET sort_order = $o WHERE id = $id";
        upd2.Parameters.AddWithValue("$o", order); upd2.Parameters.AddWithValue("$id", neighborId);
        upd2.ExecuteNonQuery();
    }

    /// <summary>Lightweight hymn search for the service builder's add-hymn panel.</summary>
    public List<HymnSummary> QuickSearchHymns(string query, int limit = 20)
    {
        var results = new List<HymnSummary>();
        if (string.IsNullOrWhiteSpace(query)) return results;
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT id, title, author, year, meter, first_line FROM texts
                             WHERE title LIKE $q OR author LIKE $q OR first_line LIKE $q
                             ORDER BY title LIMIT $limit";
        cmd.Parameters.AddWithValue("$q", $"%{query}%");
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new HymnSummary
            {
                Id = r.GetInt32(0), Title = Safe(r, "title"), Author = Safe(r, "author"),
                Year = SafeInt(r, "year"), Meter = Safe(r, "meter"), FirstLine = Safe(r, "first_line"),
                Slug = Slugify(Safe(r, "title")),
            });
        return results;
    }

    // ── SITEMAP ───────────────────────────────────────────────────────────────

    public List<string> GetSitemapUrls()
    {
        var urls = new List<string>
        {
            "/", "/hymns", "/tunes", "/people", "/people/countries", "/first-lines", "/meters",
            "/law-gospel", "/compare-hymnals", "/bible", "/lectionary", "/hymn-of-the-day",
            "/bach-cantatas", "/glossary", "/confessions", "/service-builder", "/resources", "/about", "/donate",
        };

        using var conn = GetConnection();

        var t = conn.CreateCommand();
        t.CommandText = "SELECT title FROM texts";
        using (var r = t.ExecuteReader()) { while (r.Read()) urls.Add($"/hymns/{Slugify(r.GetString(0))}"); }

        var tu = conn.CreateCommand();
        tu.CommandText = "SELECT tune_name FROM tunes";
        using (var r = tu.ExecuteReader()) { while (r.Read()) urls.Add($"/tunes/{Slugify(r.GetString(0))}"); }

        var p = conn.CreateCommand();
        p.CommandText = "SELECT name FROM people";
        using (var r = p.ExecuteReader()) { while (r.Read()) urls.Add($"/people/{Slugify(r.GetString(0))}"); }

        var cs = conn.CreateCommand();
        cs.CommandText = "SELECT id FROM confessional_sections";
        using (var r = cs.ExecuteReader()) { while (r.Read()) urls.Add($"/confessions/section/{r.GetInt32(0)}"); }

        var bb = conn.CreateCommand();
        bb.CommandText = "SELECT name, chapter_count FROM bible_books";
        using (var r = bb.ExecuteReader())
        {
            while (r.Read())
            {
                var slug = Slugify(r.GetString(0));
                var chapters = r.GetInt32(1);
                for (int c = 1; c <= chapters; c++) urls.Add($"/bible/{slug}/{c}");
            }
        }

        var lec = conn.CreateCommand();
        lec.CommandText = "SELECT year_cycle, id FROM lectionary";
        using (var r = lec.ExecuteReader()) { while (r.Read()) urls.Add($"/lectionary/{r.GetString(0)}/{r.GetInt32(1)}"); }

        return urls;
    }

    // ── HISTORICAL TIMELINE ───────────────────────────────────────────────────

    public List<TimelineEvent> GetTimeline()
    {
        var results = new List<TimelineEvent>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT year_label, title, description, image_url, image_credit FROM timeline_events ORDER BY sort_year";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new TimelineEvent
            {
                YearLabel = Safe(r, "year_label"), Title = Safe(r, "title"),
                Description = Safe(r, "description"), ImageUrl = Safe(r, "image_url"),
                ImageCredit = Safe(r, "image_credit"),
            });
        return results;
    }

    // ── TOPICAL INDEX ─────────────────────────────────────────────────────────

    public List<ThemeGroup> GetThemeIndex(string query, int limit = 60)
    {
        var results = new List<ThemeGroup>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT LOWER(theme) as norm, COUNT(*) as cnt
            FROM themes
            WHERE entity_type = 'text' AND ($q = '' OR LOWER(theme) LIKE $q)
            GROUP BY norm
            ORDER BY cnt DESC, norm
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$q", string.IsNullOrWhiteSpace(query) ? "" : $"%{query.ToLower()}%");
        cmd.Parameters.AddWithValue("$limit", limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var norm = r.GetString(0);
            var display = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(norm);
            results.Add(new ThemeGroup { ThemeKey = norm, ThemeLabel = display, Count = r.GetInt32(1) });
        }
        return results;
    }

    public (string Label, List<HymnSummary> Hymns) GetHymnsForTheme(string themeKey)
    {
        var hymns = new List<HymnSummary>();
        using var conn = GetConnection();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT t.id, t.title, t.author, t.year, t.meter, t.first_line
            FROM themes th
            JOIN texts t ON th.entity_type = 'text' AND th.entity_id = t.id
            WHERE LOWER(th.theme) = $key
            ORDER BY t.title";
        cmd.Parameters.AddWithValue("$key", themeKey.ToLower());
        using var r = cmd.ExecuteReader();
        while (r.Read())
            hymns.Add(new HymnSummary
            {
                Id = r.GetInt32(0), Title = Safe(r, "title"), Author = Safe(r, "author"),
                Year = SafeInt(r, "year"), Meter = Safe(r, "meter"), FirstLine = Safe(r, "first_line"),
                Slug = Slugify(Safe(r, "title")),
            });
        var label = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(themeKey.ToLower());
        return (label, hymns);
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

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

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
    public string Source { get; set; } = "";
    public string Slot { get; set; } = "";
    public string MatchType { get; set; } = "";
    public string MatchedRef { get; set; } = "";
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
    public string Arc { get; set; } = "";
    public string Slug { get; set; } = "";
    public List<HymnSummary> LinkedTexts { get; set; } = new();
    public List<BachEntry> BachCantatas { get; set; } = new();
    public List<HymnalRef> HymnalNumbers { get; set; } = new();
}

public class BachEntry
{
    public string Bwv { get; set; } = "";
    public string Title { get; set; } = "";
    public string TitleEnglish { get; set; } = "";
    public string SundayOccasion { get; set; } = "";
    public int YearComposed { get; set; }
    public string Scoring { get; set; } = "";
    public string ChoraleTune { get; set; } = "";
    public string LectionaryConnection { get; set; } = "";
    public string Notes { get; set; } = "";
    public string TuneSlug { get; set; } = "";
}

public class MeterGroup
{
    public string Meter { get; set; } = "";
    public int HymnCount { get; set; }
    public List<TuneStub> Tunes { get; set; } = new();
}
public class TuneStub { public string TuneName { get; set; } = ""; public string Slug { get; set; } = ""; }

public class GlossaryEntry
{
    public string Term { get; set; } = "";
    public string Category { get; set; } = "";
    public string Definition { get; set; } = "";
    public string RelatedTerms { get; set; } = "";
    public string LutheranContext { get; set; } = "";
}

public class BibleBookSummary
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Abbreviation { get; set; } = "";
    public string Testament { get; set; } = "";
    public int ChapterCount { get; set; }
    public string Slug => DatabaseService.Slugify(Name);
}

public class BibleVerseText
{
    public int Verse { get; set; }
    public string Text { get; set; } = "";
    public string LutheranShort { get; set; } = "";
}

public class BibleChapterDetail
{
    public string BookName { get; set; } = "";
    public string BookSlug { get; set; } = "";
    public int BookNumber { get; set; }
    public int Chapter { get; set; }
    public int ChapterCount { get; set; }
    public string TranslationCode { get; set; } = "";
    public string TranslationLabel { get; set; } = "";
    public List<BibleVerseText> Verses { get; set; } = new();
    public string ChapterSummary { get; set; } = "";
    public string MajorTheme { get; set; } = "";
    public string KeyPeople { get; set; } = "";
    public string KeyLocation { get; set; } = "";
    public string LiturgicalUse { get; set; } = "";
    public List<HymnSummary> RelatedHymns { get; set; } = new();
}

public class PersonSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int BirthYear { get; set; }
    public int DeathYear { get; set; }
    public string Country { get; set; } = "";
    public bool HasBio { get; set; }
    public string Roles { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Dates => BirthYear > 0 ? $"{BirthYear}–{(DeathYear > 0 ? DeathYear.ToString() : "")}" : "";
    public string Era => PersonEra.GetEra(BirthYear);
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
    public string Era => PersonEra.GetEra(BirthYear);
    public List<string> Roles { get; set; } = new();
    public List<HymnSummary> TextsAuthored { get; set; } = new();
    public List<TuneStub> TunesComposed { get; set; } = new();
    public List<HymnSummary> Translations { get; set; } = new();
}

public class SiteStats { public int TextCount { get; set; } public int TuneCount { get; set; } public int PeopleCount { get; set; } public int HymnalCount { get; set; } public int ScriptureRefCount { get; set; } }

public class ConfessionalDocumentSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Year { get; set; } = "";
    public string Description { get; set; } = "";
    public int SectionCount { get; set; }
}

public class ConfessionalDocumentDetail
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Year { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ConfessionalSectionStub> Sections { get; set; } = new();
}

public class ConfessionalSectionStub
{
    public int Id { get; set; }
    public string Part { get; set; } = "";
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string DocumentName { get; set; } = "";
    public string DocumentSlug { get; set; } = "";
}

public class ConfessionalSectionDetail
{
    public int Id { get; set; }
    public string Part { get; set; } = "";
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public int DocumentId { get; set; }
    public string DocumentName { get; set; } = "";
    public string DocumentSlug { get; set; } = "";
    public List<HymnSummary> RelatedHymns { get; set; } = new();
}

public class LectionarySundaySummary
{
    public int Id { get; set; }
    public string SundayCode { get; set; } = "";
    public string SundayName { get; set; } = "";
    public string Season { get; set; } = "";
    public string Color { get; set; } = "";
}

public class LectionaryDetail
{
    public int Id { get; set; }
    public string YearCycle { get; set; } = "";
    public string SundayCode { get; set; } = "";
    public string SundayName { get; set; } = "";
    public string Season { get; set; } = "";
    public string Color { get; set; } = "";
    public string FirstReading { get; set; } = "";
    public string FirstReadingSummary { get; set; } = "";
    public string Psalm { get; set; } = "";
    public string PsalmSummary { get; set; } = "";
    public string SecondReading { get; set; } = "";
    public string SecondReadingSummary { get; set; } = "";
    public string Gospel { get; set; } = "";
    public string GospelSummary { get; set; } = "";
    public Dictionary<string, List<HymnSummary>> HymnsByMatchType { get; set; } = new();
    public Dictionary<string, Dictionary<string, List<HymnSummary>>> SuggestionsBySourceAndSlot { get; set; } = new();
}

public class HymnOfTheDayEntry
{
    public string Season { get; set; } = "";
    public string SundayName { get; set; } = "";
    public string Hymnal { get; set; } = "";
    public string HymnNumber { get; set; } = "";
    public string HymnTitle { get; set; } = "";
    public string Notes { get; set; } = "";
    public string TextSlug { get; set; } = "";
}

public class HymnalInfo
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Year { get; set; }
}

public class HymnalComparisonRow
{
    public int TextId { get; set; }
    public string Title { get; set; } = "";
    public string Number1 { get; set; } = "";
    public string Number2 { get; set; } = "";
    public string Slug { get; set; } = "";
}

public class CountryGroup
{
    public string Continent { get; set; } = "";
    public string Country { get; set; } = "";
    public int Count { get; set; }
}

public class ServicePlanSummary
{
    public int Id { get; set; }
    public string PlanDate { get; set; } = "";
    public string Note { get; set; } = "";
    public int ItemCount { get; set; }
}

public class ServicePlanItem
{
    public int ItemId { get; set; }
    public string Slot { get; set; } = "";
    public int SortOrder { get; set; }
    public HymnSummary Hymn { get; set; } = new();
}

public class ServicePlanDetail
{
    public int Id { get; set; }
    public string PlanDate { get; set; } = "";
    public string Note { get; set; } = "";
    public Dictionary<string, List<ServicePlanItem>> ItemsBySlot { get; set; } = new();
}

public class TimelineEvent
{
    public string YearLabel { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string ImageCredit { get; set; } = "";
}

public class ThemeGroup
{
    public string ThemeKey { get; set; } = "";
    public string ThemeLabel { get; set; } = "";
    public int Count { get; set; }
}

public class SeasonInfo
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Character { get; set; } = "";
    public string Description { get; set; } = "";
}
