using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Topics;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<ThemeGroup> Themes { get; set; } = new();
    public string Query { get; set; } = "";
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet(string? q)
    {
        Query = q ?? "";
        Themes = _db.GetThemeIndex(Query, string.IsNullOrWhiteSpace(Query) ? 60 : 200);
    }
}
