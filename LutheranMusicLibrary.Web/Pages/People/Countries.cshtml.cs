using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.People;
public class CountriesModel : PageModel
{
    private readonly DatabaseService _db;
    public List<CountryGroup> Countries { get; set; } = new();
    public CountriesModel(DatabaseService db) => _db = db;
    public void OnGet() => Countries = _db.GetCountryIndex();
}
