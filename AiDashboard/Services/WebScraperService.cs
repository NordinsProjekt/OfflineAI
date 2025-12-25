using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace AiDashboard.Services;

/// <summary>
/// Service for scraping web pages and converting them to LLM-friendly context.
/// </summary>
public class WebScraperService : IWebScraperService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WebScraperService>? _logger;

    private static readonly string[] ExcludedTags = { "script", "style", "nav", "footer", "header", "aside", "iframe", "noscript" };
    private static readonly int DefaultMaxLength = 10000;

    public WebScraperService(IHttpClientFactory httpClientFactory, ILogger<WebScraperService>? logger = null)
    {
        _httpClient = httpClientFactory.CreateClient("WebScraper");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _logger = logger;
    }

    public bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            return false;

        return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
    }

    public async Task<WebScraperResult> ScrapeAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = new WebScraperResult { Url = url };

        try
        {
            if (!IsValidUrl(url))
            {
                result.Success = false;
                result.ErrorMessage = "Invalid URL format";
                return result;
            }

            _logger?.LogInformation("Scraping URL: {Url}", url);

            var response = await _httpClient.GetAsync(url, cancellationToken);
            result.StatusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                result.Success = false;
                result.ErrorMessage = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                _logger?.LogWarning("Failed to scrape {Url}: {StatusCode}", url, response.StatusCode);
                return result;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            
            var htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(html);

            result.Title = ExtractTitle(htmlDoc);
            result.Metadata = ExtractMetadata(htmlDoc);
            result.Headers = ExtractHeaders(htmlDoc);
            result.Links = ExtractLinks(htmlDoc, url);
            result.TextContent = ExtractTextContent(htmlDoc);
            result.Success = true;

            _logger?.LogInformation("Successfully scraped {Url} - Title: {Title}, Content length: {Length}", 
                url, result.Title, result.TextContent.Length);

            return result;
        }
        catch (HttpRequestException ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Network error: {ex.Message}";
            _logger?.LogError(ex, "HTTP request error while scraping {Url}", url);
            return result;
        }
        catch (TaskCanceledException ex)
        {
            result.Success = false;
            result.ErrorMessage = "Request timeout";
            _logger?.LogError(ex, "Timeout while scraping {Url}", url);
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
            _logger?.LogError(ex, "Unexpected error while scraping {Url}", url);
            return result;
        }
    }

    public async Task<string> ScrapeAsLlmContextAsync(string url, int maxLength = 0, CancellationToken cancellationToken = default)
    {
        var result = await ScrapeAsync(url, cancellationToken);

        if (!result.Success)
        {
            return $"[Web Scraping Error]\nURL: {url}\nError: {result.ErrorMessage}\n";
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Web Content Context");
        sb.AppendLine();
        sb.AppendLine($"**Source URL:** {result.Url}");
        sb.AppendLine($"**Scraped At:** {result.ScrapedAt:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        if (!string.IsNullOrEmpty(result.Title))
        {
            sb.AppendLine($"## {result.Title}");
            sb.AppendLine();
        }

        if (result.Metadata.TryGetValue("description", out var description) && !string.IsNullOrEmpty(description))
        {
            sb.AppendLine($"**Description:** {description}");
            sb.AppendLine();
        }

        if (result.Headers.Any())
        {
            sb.AppendLine("### Content Structure");
            foreach (var header in result.Headers.Take(10))
            {
                sb.AppendLine($"- {header}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("### Main Content");
        sb.AppendLine();
        sb.AppendLine(result.TextContent);

        var context = sb.ToString();

        if (maxLength > 0 && context.Length > maxLength)
        {
            const string truncationMessage = "\n\n[Content truncated due to length...]";
            var truncateAt = maxLength - truncationMessage.Length;
            if (truncateAt > 0)
            {
                context = context.Substring(0, truncateAt) + truncationMessage;
            }
            else
            {
                // If maxLength is too small to even fit the message, just truncate hard
                context = context.Substring(0, maxLength);
            }
        }

        return context;
    }

    private string ExtractTitle(HtmlDocument doc)
    {
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
            return HtmlEntity.DeEntitize(titleNode.InnerText).Trim();

        var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
        if (h1Node != null)
            return HtmlEntity.DeEntitize(h1Node.InnerText).Trim();

        return "Untitled Page";
    }

    private Dictionary<string, string> ExtractMetadata(HtmlDocument doc)
    {
        var metadata = new Dictionary<string, string>();

        var metaTags = doc.DocumentNode.SelectNodes("//meta[@name or @property]");
        if (metaTags != null)
        {
            foreach (var meta in metaTags)
            {
                var name = meta.GetAttributeValue("name", null) ?? meta.GetAttributeValue("property", null);
                var content = meta.GetAttributeValue("content", null);

                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(content))
                {
                    var key = name.ToLower().Replace("og:", "").Replace("twitter:", "");
                    if (!metadata.ContainsKey(key))
                    {
                        metadata[key] = HtmlEntity.DeEntitize(content).Trim();
                    }
                }
            }
        }

        return metadata;
    }

    private List<string> ExtractHeaders(HtmlDocument doc)
    {
        var headers = new List<string>();
        var headerTags = new[] { "h1", "h2", "h3", "h4", "h5", "h6" };

        foreach (var tag in headerTags)
        {
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    var text = HtmlEntity.DeEntitize(node.InnerText).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        headers.Add($"{tag.ToUpper()}: {text}");
                    }
                }
            }
        }

        return headers;
    }

    private List<string> ExtractLinks(HtmlDocument doc, string baseUrl)
    {
        var links = new List<string>();
        var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");

        if (linkNodes != null)
        {
            foreach (var link in linkNodes)
            {
                var href = link.GetAttributeValue("href", null);
                if (!string.IsNullOrEmpty(href))
                {
                    try
                    {
                        var absoluteUrl = new Uri(new Uri(baseUrl), href).ToString();
                        if (!links.Contains(absoluteUrl))
                        {
                            links.Add(absoluteUrl);
                        }
                    }
                    catch
                    {
                        // Invalid URL, skip
                    }
                }
            }
        }

        return links;
    }

    private string ExtractTextContent(HtmlDocument doc)
    {
        var body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

        foreach (var tag in ExcludedTags)
        {
            var nodes = body.SelectNodes($"//{tag}");
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    node.Remove();
                }
            }
        }

        var article = body.SelectSingleNode("//article") ?? 
                     body.SelectSingleNode("//main") ?? 
                     body.SelectSingleNode("//*[@id='content']") ??
                     body.SelectSingleNode("//*[@class='content']") ??
                     body;

        var text = HtmlEntity.DeEntitize(article.InnerText);
        
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"\n\s*\n\s*\n+", "\n\n");
        
        return text.Trim();
    }
}
