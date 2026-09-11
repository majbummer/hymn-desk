using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Tunes;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<TuneSummary> Tunes { get; set; } = new();
    public string Query { get; set; } = "";
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet(string? q) { Query = q ?? ""; Tunes = _db.SearchTunes(Query); }
}
