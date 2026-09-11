using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public SiteStats Stats { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Stats = _db.GetStats();
}
