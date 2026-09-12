using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Bible;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<BibleBookSummary> Books { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Books = _db.GetBibleBooks();
}
