using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.ServiceBuilder;
public class EditModel : PageModel
{
    private readonly DatabaseService _db;
    public ServicePlanDetail? Plan { get; set; }
    public string Query { get; set; } = "";
    public List<HymnSummary> SearchResults { get; set; } = new();

    public EditModel(DatabaseService db) => _db = db;

    public IActionResult OnGet(int id, string? q)
    {
        Plan = _db.GetServicePlan(id);
        if (Plan == null) return NotFound();
        Query = q ?? "";
        if (!string.IsNullOrWhiteSpace(Query)) SearchResults = _db.QuickSearchHymns(Query);
        return Page();
    }

    public IActionResult OnPostAddItem(int id, string slot, int textId, string? q)
    {
        _db.AddServicePlanItem(id, slot, textId);
        return RedirectToPage(new { id, q });
    }

    public IActionResult OnPostRemove(int id, int itemId, string? q)
    {
        _db.RemoveServicePlanItem(itemId);
        return RedirectToPage(new { id, q });
    }

    public IActionResult OnPostMove(int id, int itemId, string direction, string? q)
    {
        _db.MoveServicePlanItem(itemId, direction);
        return RedirectToPage(new { id, q });
    }
}
