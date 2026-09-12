public class PaginationInfo
{
    public int Page { get; set; }
    public int TotalPages { get; set; }
    /// <summary>The page's own path plus any filter query string, minus the "page" param — e.g. "/hymns?q=grace&amp;season=Lent&amp;"</summary>
    public string BaseQuery { get; set; } = "";
}
