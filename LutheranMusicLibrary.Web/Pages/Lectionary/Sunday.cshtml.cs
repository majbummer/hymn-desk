using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Lectionary;
public class SundayModel : PageModel
{
    private readonly DatabaseService _db;
    public LectionaryDetail? Detail { get; set; }
    public SundayModel(DatabaseService db) => _db = db;
    public IActionResult OnGet(string year, int id)
    {
        Detail = _db.GetLectionaryDetail(id);
        if (Detail == null || Detail.YearCycle != year) return NotFound();
        return Page();
    }
}
