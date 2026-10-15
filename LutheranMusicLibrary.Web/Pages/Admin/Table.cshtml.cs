using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
namespace LutheranMusicLibrary.Web.Pages.Admin;

public class TableModel : PageModel
{
    private readonly AdminService _admin;
    public TableModel(AdminService admin) => _admin = admin;

    public const int PageSize = 50;

    public string TableName { get; set; } = "";
    public string Query { get; set; } = "";
    public int PageNumber { get; set; } = 1;
    public long Total { get; set; }
    public List<AdminColumn> Cols { get; set; } = new();
    public List<AdminRow> Rows { get; set; } = new();
    public Dictionary<string, string> Filters { get; set; } = new();
    public string? Error { get; set; }
    public int TotalPages => (int)Math.Max(1, Math.Ceiling(Total / (double)PageSize));

    // Query string pieces like f_text_id=5 narrow the list to matching rows.
    public string FilterQuery() =>
        string.Concat(Filters.Select(f => $"&f_{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));

    public IActionResult OnGet(string table, string? q, int page = 1)
    {
        TableName = table;
        Query = q ?? "";
        PageNumber = Math.Max(1, page);
        foreach (var kv in Request.Query)
            if (kv.Key.StartsWith("f_") && kv.Key.Length > 2)
                Filters[kv.Key.Substring(2)] = kv.Value.ToString();

        try
        {
            var res = _admin.GetRows(table, Query, Filters, PageNumber, PageSize);
            Cols = res.Cols;
            Rows = res.Rows;
            Total = res.Total;
        }
        catch (ArgumentException) { return NotFound(); }
        catch (SqliteException ex) { Error = ex.Message; }
        return Page();
    }
}
