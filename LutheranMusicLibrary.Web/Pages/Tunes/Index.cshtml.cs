using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Tunes;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public PagedResult<TuneSummary> Result { get; set; } = new();
    public string Query { get; set; } = "";
    public string Sort { get; set; } = "";
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet(string? q, string? sort, int page = 1)
    {
        Query = q ?? "";
        Sort = sort ?? "";
        Result = _db.SearchTunes(Query, Sort, page, 50);
    }
}
