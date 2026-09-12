using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.CompareHymnals;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<HymnalInfo> Hymnals { get; set; } = new();
    public List<HymnalComparisonRow> Rows { get; set; } = new();
    public string Code1 { get; set; } = "";
    public string Code2 { get; set; } = "";

    public IndexModel(DatabaseService db) => _db = db;

    public void OnGet(string? h1, string? h2)
    {
        Hymnals = _db.GetHymnalsList();
        Code1 = h1 ?? "lsb";
        Code2 = h2 ?? "elw";
        if (Code1 != Code2)
        {
            Rows = _db.CompareHymnals(Code1, Code2);
        }
    }
}
