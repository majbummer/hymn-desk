using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Confessions;
public class SectionModel : PageModel
{
    private readonly DatabaseService _db;
    public ConfessionalSectionDetail? Detail { get; set; }
    public SectionModel(DatabaseService db) => _db = db;
    public IActionResult OnGet(int id)
    {
        Detail = _db.GetConfessionalSection(id);
        if (Detail == null) return NotFound();
        return Page();
    }
}
