namespace weesky.Snoopy.Microservice.Models.Mail;

/// <summary>One page of search results, newest first across every searched folder.</summary>
public sealed class MailSearchPage
{
    /// <summary>Total matches, all pages combined.</summary>
    public int Total { get; set; }

    /// <summary>Zero-based page index.</summary>
    public int Page { get; set; }

    public int PageSize { get; set; }

    public List<MailSearchResult> Results { get; set; } = new();
}
