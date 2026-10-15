using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
namespace LutheranMusicLibrary.Web.Pages.Admin;

public class RelatedLink
{
    public string Label { get; set; } = "";
    public string Table { get; set; } = "";
    public string Query { get; set; } = "";   // e.g. f_text_id=5
}

public class EditModel : PageModel
{
    private readonly AdminService _admin;
    public EditModel(AdminService admin) => _admin = admin;

    public string TableName { get; set; } = "";
    public long? RowId { get; set; }
    public List<AdminColumn> Cols { get; set; } = new();
    public Dictionary<string, string?> Values { get; set; } = new();
    public Dictionary<string, string> Filters { get; set; } = new();
    public List<(string Value, string Label)> HymnalOptions { get; set; } = new();
    public List<(string Value, string Label)> TextOptions { get; set; } = new();
    public List<RelatedLink> Related { get; set; } = new();
    public string? Error { get; set; }
    public bool Saved { get; set; }

    public string FilterQuery() =>
        string.Join("&", Filters.Select(f => $"f_{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

    private bool LoadSchema(string table)
    {
        TableName = table;
        try { Cols = _admin.GetColumns(table); }
        catch (ArgumentException) { return false; }
        if (Cols.Any(c => c.Name == "hymnal_id")) HymnalOptions = _admin.GetHymnalOptions();
        if (Cols.Any(c => c.Name == "text_id")) TextOptions = _admin.GetTextOptions();
        return true;
    }

    private void BuildRelated()
    {
        if (RowId == null) return;
        var id = RowId.Value;
        var existing = _admin.ListTables().Select(t => t.Name).ToHashSet();
        void Add(string label, string table, string query)
        {
            if (existing.Contains(table)) Related.Add(new RelatedLink { Label = label, Table = table, Query = query });
        }
        if (TableName == "texts")
        {
            Add("Hymnal numbers", "text_hymnal_numbers", $"f_text_id={id}");
            Add("Tunes", "text_tunes", $"f_text_id={id}");
            Add("Scripture references", "scripture_refs", $"f_entity_type=text&f_entity_id={id}");
            Add("Seasons", "seasons", $"f_entity_type=text&f_entity_id={id}");
            Add("Themes", "themes", $"f_entity_type=text&f_entity_id={id}");
            Add("Reference notes", "reference_notes", $"f_entity_type=text&f_entity_id={id}");
            Add("Devotions", "devotions", $"f_text_id={id}");
        }
        else if (TableName == "tunes")
        {
            Add("Hymnal numbers", "tune_hymnal_numbers", $"f_tune_id={id}");
            Add("Texts using this tune", "text_tunes", $"f_tune_id={id}");
        }
    }

    private void ReadFilters()
    {
        if (Request.HasFormContentType)
            foreach (var kv in Request.Form)
                if (kv.Key.StartsWith("f_") && kv.Key.Length > 2)
                    Filters[kv.Key.Substring(2)] = kv.Value.ToString();
        foreach (var kv in Request.Query)
            if (kv.Key.StartsWith("f_") && kv.Key.Length > 2 && !Filters.ContainsKey(kv.Key.Substring(2)))
                Filters[kv.Key.Substring(2)] = kv.Value.ToString();
    }

    public IActionResult OnGet(string table, long? id, bool saved = false)
    {
        if (!LoadSchema(table)) return NotFound();
        RowId = id;
        Saved = saved;
        ReadFilters();

        if (id != null)
        {
            var row = _admin.GetRow(table, id.Value);
            if (row == null) return NotFound();
            for (int i = 0; i < Cols.Count; i++) Values[Cols[i].Name] = row.Values[i];
            BuildRelated();
        }
        else
        {
            // New row: start with any values carried over from the list we came from.
            foreach (var f in Filters)
                if (Cols.Any(c => c.Name == f.Key)) Values[f.Key] = f.Value;
        }
        return Page();
    }

    private Dictionary<string, string?> ReadForm()
    {
        var values = new Dictionary<string, string?>();
        foreach (var c in Cols)
        {
            var key = "col_" + c.Name;
            if (Request.Form.ContainsKey(key)) values[c.Name] = Request.Form[key].ToString();
        }
        return values;
    }

    public IActionResult OnPost(string table, long? id)
    {
        if (!LoadSchema(table)) return NotFound();
        RowId = id;
        ReadFilters();
        var values = ReadForm();
        var next = Request.Form["next"].ToString();

        try
        {
            long savedId;
            if (id == null) savedId = _admin.Insert(table, values);
            else { _admin.Update(table, id.Value, values); savedId = id.Value; }

            var fq = FilterQuery();
            var tableUrl = "/admin/" + Uri.EscapeDataString(table);
            if (next == "list")
                return Redirect(tableUrl + (fq.Length > 0 ? "?" + fq : ""));
            if (next == "another")
                return Redirect(tableUrl + "/edit" + (fq.Length > 0 ? "?" + fq + "&saved=true" : "?saved=true"));
            return Redirect($"{tableUrl}/edit/{savedId}?saved=true" + (fq.Length > 0 ? "&" + fq : ""));
        }
        catch (SqliteException ex)
        {
            // Show the problem and keep what was typed.
            Error = ex.Message;
            Values = values;
            if (id != null) BuildRelated();
            return Page();
        }
    }

    public IActionResult OnPostDelete(string table, long id)
    {
        if (!LoadSchema(table)) return NotFound();
        ReadFilters();
        try { _admin.Delete(table, id); }
        catch (SqliteException ex)
        {
            Error = ex.Message;
            RowId = id;
            var row = _admin.GetRow(table, id);
            if (row != null) for (int i = 0; i < Cols.Count; i++) Values[Cols[i].Name] = row.Values[i];
            BuildRelated();
            return Page();
        }
        var fq = FilterQuery();
        return Redirect("/admin/" + Uri.EscapeDataString(table) + (fq.Length > 0 ? "?" + fq : ""));
    }
}
