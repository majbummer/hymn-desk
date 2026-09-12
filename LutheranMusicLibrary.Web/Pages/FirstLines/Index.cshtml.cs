using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.FirstLines;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<HymnSummary> Hymns { get; set; } = new();
    public string Query { get; set; } = "";
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet(string? q)
    {
        Query = q ?? "";
        Hymns = _db.GetFirstLineIndex(Query);
    }
}
