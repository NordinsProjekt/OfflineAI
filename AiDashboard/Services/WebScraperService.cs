using System.Net;
using System.Net.Sockets;
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

    // Cap redirect hops so a chain can't be used to stall the request or evade the per-hop check.
    private const int MaxRedirects = 5;

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

    /// <summary>
    /// Issues the GET while defending against SSRF: it re-checks the scheme and resolves the host
    /// on every hop, blocking any URL that resolves to a loopback/private/link-local/reserved
    /// address, and follows redirects manually so a public URL can't 3xx-redirect the request to
    /// an internal target. The named "WebScraper" HttpClient must be configured with
    /// AllowAutoRedirect = false (see Program.cs) for the manual, validated redirect handling here
    /// to take effect.
    /// </summary>
    private async Task<(HttpResponseMessage? Response, string? Error)> FetchWithSsrfGuardAsync(
        string url, CancellationToken cancellationToken)
    {
        var currentUrl = url;

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return (null, "Invalid or non-HTTP(S) URL in the redirect chain.");
            }

            if (!await IsHostAllowedAsync(uri, cancellationToken))
            {
                return (null, "The URL resolves to a private, loopback, or otherwise non-public address and was blocked.");
            }

            var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (IsRedirect(response.StatusCode) && response.Headers.Location is not null)
            {
                var location = response.Headers.Location;
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                currentUrl = next.ToString();
                continue;
            }

            return (response, null);
        }

        return (null, $"Too many redirects (more than {MaxRedirects}).");
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found
            or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Resolves the host (or parses it as an IP literal) and returns false if any resolved address
    /// is in a range that must not be reachable via the scraper.
    /// </summary>
    private static async Task<bool> IsHostAllowedAsync(Uri uri, CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.Host, out var literal))
        {
            addresses = new[] { literal };
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            }
            catch (SocketException)
            {
                return false;
            }
        }

        return addresses.Length > 0 && addresses.All(a => !IsBlockedAddress(a));
    }

    /// <summary>
    /// True for loopback, private (RFC1918), link-local, CGNAT, multicast/reserved, and IPv6
    /// unique-local/link-local addresses — i.e. anything that shouldn't be reachable through a
    /// user-supplied scrape URL.
    /// </summary>
    private static bool IsBlockedAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 0                                    // 0.0.0.0/8 ("this network")
                || b[0] == 10                                   // 10.0.0.0/8 private
                || b[0] == 127                                  // 127.0.0.0/8 loopback
                || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 link-local (incl. cloud metadata)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12 private
                || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16 private
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)   // 100.64.0.0/10 CGNAT
                || b[0] >= 224;                                 // 224.0.0.0/4 multicast + 240/4 reserved
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast)
                return true;
            if (IPAddress.IPv6Loopback.Equals(ip))
                return true;

            var b = ip.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC; // fc00::/7 unique local
        }

        return true; // unknown address family — block by default
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

            var (response, fetchError) = await FetchWithSsrfGuardAsync(url, cancellationToken);
            if (response is null)
            {
                result.Success = false;
                result.ErrorMessage = fetchError ?? "Request blocked";
                _logger?.LogWarning("Blocked or failed scrape of {Url}: {Error}", url, fetchError);
                return result;
            }

            using var _ = response;
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
            context = truncateAt > 0 
                ? context.Substring(0, truncateAt) + truncationMessage
                : context.Substring(0, maxLength); // If maxLength is too small to even fit the message, just truncate hard
        }

        return context;
    }

    private static string ExtractTitle(HtmlDocument doc)
    {
        var titleNode = doc.DocumentNode.SelectSingleNode("//title");
        if (titleNode != null)
            return HtmlEntity.DeEntitize(titleNode.InnerText).Trim();

        var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
        if (h1Node != null)
            return HtmlEntity.DeEntitize(h1Node.InnerText).Trim();

        return "Untitled Page";
    }

    private static Dictionary<string, string> ExtractMetadata(HtmlDocument doc)
    {
        var metadata = new Dictionary<string, string>();

        var metaTags = doc.DocumentNode.SelectNodes("//meta[@name or @property]");
        if (metaTags != null)
        {
            foreach (var meta in metaTags)
            {
                var name = meta.GetAttributeValue("name", string.Empty);
                if (string.IsNullOrEmpty(name))
                    name = meta.GetAttributeValue("property", string.Empty);
                var content = meta.GetAttributeValue("content", string.Empty);

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

    private static List<string> ExtractHeaders(HtmlDocument doc)
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

    private static List<string> ExtractLinks(HtmlDocument doc, string baseUrl)
    {
        var links = new List<string>();
        var linkNodes = doc.DocumentNode.SelectNodes("//a[@href]");

        if (linkNodes != null)
        {
            foreach (var link in linkNodes)
            {
                var href = link.GetAttributeValue("href", string.Empty);
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

    private static string ExtractTextContent(HtmlDocument doc)
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
