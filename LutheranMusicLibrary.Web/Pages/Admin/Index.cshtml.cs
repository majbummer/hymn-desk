using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly AdminService _admin;
    public IndexModel(AdminService admin) => _admin = admin;

    public List<AdminTableInfo> Tables { get; set; } = new();
    public List<(string Label, string Table)> QuickAdds { get; set; } = new();

    public void OnGet()
    {
        Tables = _admin.ListTables();
        var names = Tables.Select(t => t.Name).ToHashSet();
        var wanted = new[]
        {
            ("Add hymn text", "texts"),
            ("Add hymn tune", "tunes"),
            ("Add person", "people"),
            ("Add devotion", "devotions"),
        };
        foreach (var (label, table) in wanted)
            if (names.Contains(table)) QuickAdds.Add((label, table));
    }
}
