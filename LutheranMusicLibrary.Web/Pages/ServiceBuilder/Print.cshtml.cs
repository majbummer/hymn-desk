using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.ServiceBuilder;
public class PrintModel : PageModel
{
    private readonly DatabaseService _db;
    public ServicePlanDetail? Plan { get; set; }
    public PrintModel(DatabaseService db) => _db = db;
    public IActionResult OnGet(int id)
    {
        Plan = _db.GetServicePlan(id);
        if (Plan == null) return NotFound();
        return Page();
    }
}
