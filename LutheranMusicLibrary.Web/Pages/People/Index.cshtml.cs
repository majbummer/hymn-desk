using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.People;
public class IndexModel : PageModel
{
    private readonly DatabaseService _db;
    public PagedResult<PersonSummary> Result { get; set; } = new();
    public string Query { get; set; } = "";
    public string Role { get; set; } = "";
    public string Era { get; set; } = "";
    public string Sort { get; set; } = "";
    public IndexModel(DatabaseService db) => _db = db;
    public void OnGet(string? q, string? role, string? era, string? sort, int page = 1)
    {
        Query = q ?? "";
        Role = role ?? "";
        Era = era ?? "";
        Sort = sort ?? "";
        Result = _db.SearchPeople(Query, Role, Era, Sort, page, 50);
    }
}
