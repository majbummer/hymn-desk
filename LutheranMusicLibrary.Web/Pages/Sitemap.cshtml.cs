using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace LutheranMusicLibrary.Web.Pages;
public class SitemapModel : PageModel
{
    private readonly DatabaseService _db;
    public SitemapModel(DatabaseService db) => _db = db;

    public IActionResult OnGet()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var urls = _db.GetSitemapUrls();

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var path in urls)
        {
            var loc = System.Security.SecurityElement.Escape(baseUrl + path);
            sb.Append($"  <url><loc>{loc}</loc></url>\n");
        }
        sb.Append("</urlset>\n");

        return Content(sb.ToString(), "application/xml");
    }
}
