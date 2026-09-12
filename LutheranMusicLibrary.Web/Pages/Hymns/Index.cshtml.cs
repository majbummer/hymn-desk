using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Hymns;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public PagedResult<HymnSummary> Result { get; set; } = new();
    public string Query { get; set; } = "";
    public string Season { get; set; } = "";
    public string Meter { get; set; } = "";
    public string Sort { get; set; } = "";

    public IndexModel(DatabaseService db) => _db = db;

    public void OnGet(string? q, string? season, string? meter, string? sort, int page = 1)
    {
        Query = q ?? "";
        Season = season ?? "";
        Meter = meter ?? "";
        Sort = sort ?? "";
        Result = _db.SearchHymns(Query, Season, Meter, Sort, page, 50);
    }
}
