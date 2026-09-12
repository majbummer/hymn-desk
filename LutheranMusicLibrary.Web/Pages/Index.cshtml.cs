using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public SiteStats Stats { get; set; } = new();
    public SeasonInfo? Season { get; set; }
    public HymnSummary? FeaturedHymn { get; set; }

    public IndexModel(DatabaseService db) => _db = db;

    public void OnGet()
    {
        Stats = _db.GetStats();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var seasonName = LiturgicalCalendar.GetSeasonName(today);
        Season = _db.GetSeasonInfo(seasonName);

        int seed = today.Year * 1000 + today.DayOfYear;
        FeaturedHymn = _db.GetFeaturedHymnForSeason(LiturgicalCalendar.SearchTagsFor(seasonName), seed);
    }
}
