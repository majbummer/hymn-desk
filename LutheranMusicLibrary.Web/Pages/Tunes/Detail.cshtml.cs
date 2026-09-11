using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Tunes;
public class DetailModel : PageModel
{
    private readonly DatabaseService _db;
    public TuneDetail? Tune { get; set; }
    public DetailModel(DatabaseService db) => _db = db;
    public IActionResult OnGet(string slug) { Tune = _db.GetTuneBySlug(slug); if (Tune == null) return NotFound(); return Page(); }
}
