using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Confessions;
public class DocumentModel : PageModel
{
    private readonly DatabaseService _db;
    public ConfessionalDocumentDetail? Detail { get; set; }
    public DocumentModel(DatabaseService db) => _db = db;
    public IActionResult OnGet(string slug)
    {
        Detail = _db.GetConfessionalDocumentBySlug(slug);
        if (Detail == null) return NotFound();
        return Page();
    }
}
