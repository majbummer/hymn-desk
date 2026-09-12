using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LutheranMusicLibrary.Web.Pages.Hymns;

public class DetailModel : PageModel
{
    private readonly DatabaseService _db;
    public HymnDetail? Hymn { get; set; }
    public List<ConfessionalSectionStub> ConfessionalConnections { get; set; } = new();
    public string? StanzaArc { get; set; }

    public DetailModel(DatabaseService db) => _db = db;

    public IActionResult OnGet(string slug)
    {
        Hymn = _db.GetHymnBySlug(slug);
        if (Hymn == null) return NotFound();
        ConfessionalConnections = _db.GetConfessionalSectionsForHymn(Hymn.Id);
        StanzaArc = _db.GetStanzaArcForHymn(Hymn.Id);
        return Page();
    }
}
