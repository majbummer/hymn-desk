using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LutheranMusicLibrary.Web.Pages.Hymns;

public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<HymnSummary> Hymns { get; set; } = new();
    public string Query { get; set; } = "";
    public string Season { get; set; } = "";

    public IndexModel(DatabaseService db) => _db = db;

    public void OnGet(string? q, string? season, string? meter)
    {
        Query = q ?? "";
        Season = season ?? "";
        Hymns = _db.SearchHymns(Query, Season, meter ?? "");
    }
}
