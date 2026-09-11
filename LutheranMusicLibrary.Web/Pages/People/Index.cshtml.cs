using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.People;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public List<PersonSummary> People { get; set; } = new();
    public string Query { get; set; } = "";
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet(string? q) { Query = q ?? ""; People = _db.SearchPeople(Query); }
}
