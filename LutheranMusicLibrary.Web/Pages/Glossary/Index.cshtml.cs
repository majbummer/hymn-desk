using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Glossary;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<GlossaryEntry> Terms { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Terms = _db.GetGlossary();
}
