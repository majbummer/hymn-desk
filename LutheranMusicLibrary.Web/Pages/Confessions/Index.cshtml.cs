using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Confessions;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<ConfessionalDocumentSummary> Documents { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Documents = _db.GetConfessionalDocuments();
}
