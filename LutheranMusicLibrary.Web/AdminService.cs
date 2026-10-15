using System.Globalization;
using Microsoft.Data.Sqlite;

// Local-only data entry for the SQLite database. Reads the real schema with
// PRAGMA table_info, so it works with whatever columns the tables have today
// and keeps working if you add columns later. Every table and column name from
// the browser is checked against the live schema before it is used in SQL, and
// every value is passed as a parameter.

public class AdminColumn
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool NotNull { get; set; }
    public bool IsPk { get; set; }
    public string? Default { get; set; }

    public bool IsInteger => Type.Contains("INT", StringComparison.OrdinalIgnoreCase);
}

public class AdminRow
{
    public long RowId { get; set; }
    public List<string?> Values { get; set; } = new();
}

public class AdminTableInfo
{
    public string Name { get; set; } = "";
    public long Rows { get; set; }
}

public class AdminService
{
    private readonly string _dbPath;
    private readonly object _backupLock = new();
    private bool _backedUp;

    public AdminService(string dbPath) => _dbPath = dbPath;

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    // Quote an identifier. Names are already validated against the schema;
    // this is a second line of defence.
    private static string Q(string ident) => "\"" + ident.Replace("\"", "\"\"") + "\"";

    private static string? Str(SqliteDataReader r, int i)
    {
        if (r.IsDBNull(i)) return null;
        var v = r.GetValue(i);
        if (v is byte[]) return "[binary data]";
        return Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    private static List<string> TableNames(SqliteConnection conn)
    {
        var names = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var r = cmd.ExecuteReader();
        while (r.Read()) names.Add(r.GetString(0));
        return names;
    }

    private static string RequireTable(SqliteConnection conn, string table)
    {
        var match = TableNames(conn).FirstOrDefault(n => n == table);
        if (match == null) throw new ArgumentException("Unknown table.");
        return match;
    }

    private static List<AdminColumn> Columns(SqliteConnection conn, string table)
    {
        var cols = new List<AdminColumn>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({Q(table)})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            cols.Add(new AdminColumn
            {
                Name = r.GetString(1),
                Type = r.IsDBNull(2) ? "" : r.GetString(2),
                NotNull = r.GetInt32(3) == 1,
                Default = r.IsDBNull(4) ? null : r.GetString(4),
                IsPk = r.GetInt32(5) > 0,
            });
        }
        return cols;
    }

    // ── Table list ────────────────────────────────────────────────────────────

    public List<AdminTableInfo> ListTables()
    {
        using var conn = Open();
        var result = new List<AdminTableInfo>();
        foreach (var name in TableNames(conn))
        {
            long count;
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {Q(name)}";
                count = Convert.ToInt64(cmd.ExecuteScalar());
            }
            catch { count = -1; }
            result.Add(new AdminTableInfo { Name = name, Rows = count });
        }
        return result;
    }

    public bool TableExists(string table)
    {
        using var conn = Open();
        return TableNames(conn).Contains(table);
    }

    public List<AdminColumn> GetColumns(string table)
    {
        using var conn = Open();
        RequireTable(conn, table);
        return Columns(conn, table);
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    public (List<AdminColumn> Cols, List<AdminRow> Rows, long Total) GetRows(
        string table, string search, Dictionary<string, string> filters, int page, int pageSize)
    {
        using var conn = Open();
        RequireTable(conn, table);
        var cols = Columns(conn, table);

        var where = new List<string>();
        var parms = new List<(string, object)>();

        int n = 0;
        foreach (var f in filters)
        {
            var col = cols.FirstOrDefault(c => c.Name == f.Key);
            if (col == null) continue;
            var p = $"$f{n++}";
            where.Add($"{Q(col.Name)} = {p}");
            parms.Add((p, f.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var ors = cols.Select(c => $"CAST({Q(c.Name)} AS TEXT) LIKE $q");
            where.Add("(" + string.Join(" OR ", ors) + ")");
            parms.Add(("$q", $"%{search}%"));
        }

        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        long total;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT COUNT(*) FROM {Q(table)} {whereSql}";
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
            total = Convert.ToInt64(cmd.ExecuteScalar());
        }

        var rows = new List<AdminRow>();
        using (var cmd = conn.CreateCommand())
        {
            // Newest rows first, so what you just added is at the top.
            cmd.CommandText = $"SELECT rowid, * FROM {Q(table)} {whereSql} ORDER BY rowid DESC LIMIT $take OFFSET $skip";
            foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
            cmd.Parameters.AddWithValue("$take", pageSize);
            cmd.Parameters.AddWithValue("$skip", (Math.Max(1, page) - 1) * pageSize);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var row = new AdminRow { RowId = r.GetInt64(0) };
                for (int i = 1; i <= cols.Count; i++) row.Values.Add(Str(r, i));
                rows.Add(row);
            }
        }
        return (cols, rows, total);
    }

    public AdminRow? GetRow(string table, long rowId)
    {
        using var conn = Open();
        RequireTable(conn, table);
        var cols = Columns(conn, table);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT rowid, * FROM {Q(table)} WHERE rowid = $id";
        cmd.Parameters.AddWithValue("$id", rowId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var row = new AdminRow { RowId = r.GetInt64(0) };
        for (int i = 1; i <= cols.Count; i++) row.Values.Add(Str(r, i));
        return row;
    }

    // ── Lookups used for dropdowns ────────────────────────────────────────────

    public List<(string Value, string Label)> GetHymnalOptions()
    {
        var list = new List<(string, string)>();
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, code, name FROM hymnals ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((r.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                          $"{(r.IsDBNull(1) ? "" : r.GetString(1))} — {(r.IsDBNull(2) ? "" : r.GetString(2))}"));
        }
        catch { /* hymnals table missing or shaped differently: fall back to a plain box */ }
        return list;
    }

    public List<(string Value, string Label)> GetTextOptions()
    {
        var list = new List<(string, string)>();
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, title, author FROM texts ORDER BY title";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var title = r.IsDBNull(1) ? "" : r.GetString(1);
                var author = r.IsDBNull(2) ? "" : r.GetString(2);
                list.Add((r.GetInt64(0).ToString(CultureInfo.InvariantCulture),
                          author == "" ? title : $"{title} — {author}"));
            }
        }
        catch { /* texts table missing or shaped differently: fall back to a plain box */ }
        return list;
    }

    // ── Writes ────────────────────────────────────────────────────────────────

    // Takes a safety copy of the whole database the first time anything is
    // written in a run of the app, and keeps the newest five copies.
    private void EnsureBackup()
    {
        lock (_backupLock)
        {
            if (_backedUp) return;
            var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_dbPath))!, "admin-backups");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, $"lutheran_music_planner-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            using (var src = Open())
            using (var dst = new SqliteConnection($"Data Source={dest}"))
            {
                dst.Open();
                src.BackupDatabase(dst);
            }
            SqliteConnection.ClearAllPools();
            try
            {
                foreach (var old in Directory.GetFiles(dir, "lutheran_music_planner-*.db")
                                             .OrderByDescending(f => f).Skip(5))
                    File.Delete(old);
            }
            catch { /* pruning is best effort */ }
            _backedUp = true;
        }
    }

    private static object ParamValue(AdminColumn col, string? raw, bool forInsert, out bool skip)
    {
        skip = false;
        if (!string.IsNullOrEmpty(raw)) return raw;

        // Blank field.
        if (forInsert)
        {
            if (col.IsPk && col.IsInteger) { skip = true; return DBNull.Value; }   // auto-number
            if (col.Default != null) { skip = true; return DBNull.Value; }          // let the DB default apply
        }
        return col.NotNull ? "" : DBNull.Value;
    }

    public long Insert(string table, Dictionary<string, string?> values)
    {
        using var conn = Open();
        RequireTable(conn, table);
        var cols = Columns(conn, table);
        EnsureBackup();

        var names = new List<string>();
        var holders = new List<string>();
        var parms = new List<(string, object)>();
        int n = 0;
        foreach (var c in cols)
        {
            values.TryGetValue(c.Name, out var raw);
            var v = ParamValue(c, raw, forInsert: true, out var skip);
            if (skip) continue;
            var p = $"$p{n++}";
            names.Add(Q(c.Name));
            holders.Add(p);
            parms.Add((p, v));
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = names.Count == 0
            ? $"INSERT INTO {Q(table)} DEFAULT VALUES"
            : $"INSERT INTO {Q(table)} ({string.Join(", ", names)}) VALUES ({string.Join(", ", holders)})";
        foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(idCmd.ExecuteScalar());
    }

    public void Update(string table, long rowId, Dictionary<string, string?> values)
    {
        using var conn = Open();
        RequireTable(conn, table);
        var cols = Columns(conn, table);
        EnsureBackup();

        var sets = new List<string>();
        var parms = new List<(string, object)>();
        int n = 0;
        foreach (var c in cols)
        {
            if (c.IsPk) continue;                         // ids are shown but not editable
            if (!values.TryGetValue(c.Name, out var raw)) continue;
            var v = ParamValue(c, raw, forInsert: false, out _);
            var p = $"$p{n++}";
            sets.Add($"{Q(c.Name)} = {p}");
            parms.Add((p, v));
        }
        if (sets.Count == 0) return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {Q(table)} SET {string.Join(", ", sets)} WHERE rowid = $id";
        foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
        cmd.Parameters.AddWithValue("$id", rowId);
        cmd.ExecuteNonQuery();
    }

    public void Delete(string table, long rowId)
    {
        using var conn = Open();
        RequireTable(conn, table);
        EnsureBackup();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM {Q(table)} WHERE rowid = $id";
        cmd.Parameters.AddWithValue("$id", rowId);
        cmd.ExecuteNonQuery();
    }
}
