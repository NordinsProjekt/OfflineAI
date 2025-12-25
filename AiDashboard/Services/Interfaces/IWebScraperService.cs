namespace AiDashboard.Services;

/// <summary>
/// Interface for web scraping service that fetches and processes web page content
/// for use as context in LLM prompts.
/// </summary>
public interface IWebScraperService
{
    /// <summary>
    /// Scrapes a web page and extracts its content.
    /// </summary>
    /// <param name="url">The URL of the web page to scrape</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scraped web page result with structured content</returns>
    Task<WebScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scrapes a web page and formats it as LLM context.
    /// </summary>
    /// <param name="url">The URL of the web page to scrape</param>
    /// <param name="maxLength">Maximum length of the formatted context (0 = no limit)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Formatted context ready for LLM consumption</returns>
    Task<string> ScrapeAsLlmContextAsync(string url, int maxLength = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if a URL is safe and accessible for scraping.
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <returns>True if the URL is valid and accessible</returns>
    bool IsValidUrl(string url);
}

/// <summary>
/// Result of a web scraping operation.
/// </summary>
public class WebScraperResult
{
    /// <summary>
    /// The original URL that was scraped.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// The page title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The main text content extracted from the page.
    /// </summary>
    public string TextContent { get; set; } = string.Empty;

    /// <summary>
    /// Metadata extracted from the page (description, keywords, etc.).
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// List of headers found in the content (h1, h2, h3, etc.).
    /// </summary>
    public List<string> Headers { get; set; } = new();

    /// <summary>
    /// List of links found on the page.
    /// </summary>
    public List<string> Links { get; set; } = new();

    /// <summary>
    /// Indicates if the scraping was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if scraping failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// HTTP status code of the response.
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Timestamp of when the page was scraped.
    /// </summary>
    public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
}
