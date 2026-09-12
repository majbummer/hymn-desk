using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.CalendarWheel;
public class IndexModel : PageModel
{
    public int Year { get; set; }
    public List<WheelSegment> Segments { get; set; } = new();
    public bool ShowTodayMarker { get; set; }
    public double MarkerX1 { get; set; }
    public double MarkerY1 { get; set; }
    public double MarkerX2 { get; set; }
    public double MarkerY2 { get; set; }
    public string TodaySeasonName { get; set; } = "";

    public void OnGet(int? year)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        Year = year ?? today.Year;
        Segments = LiturgicalCalendar.GetWheelSegments(Year, 260, 260, 220, 130);
        ShowTodayMarker = Year == today.Year;
        if (ShowTodayMarker)
        {
            var angle = LiturgicalCalendar.GetDateAngle(today);
            var rad = angle * Math.PI / 180.0;
            MarkerX1 = 260 + 122 * Math.Sin(rad);
            MarkerY1 = 260 - 122 * Math.Cos(rad);
            MarkerX2 = 260 + 238 * Math.Sin(rad);
            MarkerY2 = 260 - 238 * Math.Cos(rad);
            TodaySeasonName = LiturgicalCalendar.GetSeasonName(today);
        }
    }
}
