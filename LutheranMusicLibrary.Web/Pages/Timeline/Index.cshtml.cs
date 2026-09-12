using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Timeline;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<TimelineEvent> Events { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Events = _db.GetTimeline();
}
