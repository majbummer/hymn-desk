using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.People;
public class DetailModel : PageModel
{
    private readonly DatabaseService _db;
    public PersonDetail? Person { get; set; }
    public DetailModel(DatabaseService db) => _db = db;
    public IActionResult OnGet(string slug) { Person = _db.GetPersonBySlug(slug); if (Person == null) return NotFound(); return Page(); }
}
