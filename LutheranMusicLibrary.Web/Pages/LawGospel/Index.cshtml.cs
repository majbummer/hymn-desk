using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.LawGospel;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public Dictionary<string, List<HymnSummary>> Groups { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Groups = _db.GetLawGospelIndex();
}
