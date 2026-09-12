using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.BachCantatas;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<BachEntry> Cantatas { get; set; } = new();
    public string Query { get; set; } = "";
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet(string? q)
    {
        Query = q ?? "";
        Cantatas = _db.GetBachCantatas(Query);
    }
}
