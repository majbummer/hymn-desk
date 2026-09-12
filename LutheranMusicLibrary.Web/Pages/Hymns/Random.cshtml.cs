using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Hymns;
public class RandomModel : PageModel
{
    private readonly DatabaseService _db;
    public RandomModel(DatabaseService db) => _db = db;
    public IActionResult OnGet()
    {
        var slug = _db.GetRandomHymnSlug();
        if (slug == null) return NotFound();
        return Redirect($"/hymns/{slug}");
    }
}
