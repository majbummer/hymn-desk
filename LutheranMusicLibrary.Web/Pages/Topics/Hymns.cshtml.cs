using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Topics;
public class HymnsModel : PageModel
{
    private readonly DatabaseService _db;
    public string ThemeLabel { get; set; } = "";
    public List<HymnSummary> HymnList { get; set; } = new();
    public HymnsModel(DatabaseService db) => _db = db;
    public IActionResult OnGet(string theme)
    {
        if (string.IsNullOrWhiteSpace(theme)) return RedirectToPage("Index");
        var (label, hymns) = _db.GetHymnsForTheme(theme);
        ThemeLabel = label;
        HymnList = hymns;
        return Page();
    }
}
