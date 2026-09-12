using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.ServiceBuilder;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<ServicePlanSummary> Plans { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;

    public void OnGet() => Plans = _db.GetServicePlans();

    public IActionResult OnPostCreate(string planDate, string note)
    {
        var id = _db.CreateServicePlan(planDate, note);
        return RedirectToPage("Edit", new { id });
    }

    public IActionResult OnPostDelete(int planId)
    {
        _db.DeleteServicePlan(planId);
        return RedirectToPage("Index");
    }
}
