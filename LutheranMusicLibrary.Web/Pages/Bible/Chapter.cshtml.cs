using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Bible;
public class ChapterModel : PageModel
{
    private readonly DatabaseService _db;
    public BibleChapterDetail? Detail { get; set; }
    public string Book { get; set; } = "";
    public int Chapter { get; set; }

    public ChapterModel(DatabaseService db) => _db = db;

    public IActionResult OnGet(string book, int chapter, string? t)
    {
        Book = book;
        Chapter = chapter;
        Detail = _db.GetBibleChapter(book, chapter, t ?? "kjv");
        if (Detail == null) return NotFound();
        return Page();
    }
}
