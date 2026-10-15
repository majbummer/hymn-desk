using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Devotions;

public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<DevotionSummary> Items { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Items = _db.GetDevotions();
}
