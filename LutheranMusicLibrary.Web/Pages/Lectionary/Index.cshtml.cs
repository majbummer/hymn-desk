using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Lectionary;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<LectionarySundaySummary> Sundays { get; set; } = new();
    public string YearCycle { get; set; } = "";
    public string CurrentCycle { get; set; } = "";

    public IndexModel(DatabaseService db) => _db = db;

    public void OnGet(string? year)
    {
        CurrentCycle = LiturgicalCalendar.GetCurrentLectionaryYear(DateOnly.FromDateTime(DateTime.Today));
        YearCycle = string.IsNullOrEmpty(year) ? CurrentCycle : year;
        Sundays = _db.GetLectionaryIndex(YearCycle);
    }
}
