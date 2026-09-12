using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Meters;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<MeterGroup> Meters { get; set; } = new();
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet() => Meters = _db.GetMeterIndex();
}
