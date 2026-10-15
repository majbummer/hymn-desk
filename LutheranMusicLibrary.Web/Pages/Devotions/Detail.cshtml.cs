using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Devotions;

public class DetailModel : PageModel
{
    private readonly DatabaseService _db;
    public DevotionDetail? Devotion { get; set; }
    public DetailModel(DatabaseService db) => _db = db;

    public IActionResult OnGet(string slug)
    {
        Devotion = _db.GetDevotionBySlug(slug);
        if (Devotion == null) return NotFound();
        return Page();
    }
}
