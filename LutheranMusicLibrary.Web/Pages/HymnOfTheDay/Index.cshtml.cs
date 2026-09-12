using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.HymnOfTheDay;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<HymnOfTheDayEntry> Entries { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Entries = _db.GetHymnOfTheDayCalendar();
}
