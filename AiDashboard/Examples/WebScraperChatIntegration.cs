using AiDashboard.Services;
using Entities;
using Services.Configuration;

namespace AiDashboard.Examples;

/// <summary>
/// Example demonstrating how to integrate the Web Scraper Service
/// with the existing LLM chat functionality.
/// NOTE: This is a simplified example. In production, you would inject
/// or access the actual DashboardState to get current settings.
/// </summary>
public class WebScraperChatIntegration
{
    private readonly IWebScraperService _webScraper;
    private readonly DashboardChatService _chatService;
    private readonly GenerationSettings _defaultSettings;

    public WebScraperChatIntegration(
        IWebScraperService webScraper,
        DashboardChatService chatService)
    {
        _webScraper = webScraper;
        _chatService = chatService;
        
        // Default generation settings for examples
        _defaultSettings = new GenerationSettings
        {
            Temperature = 0.7f,
            MaxTokens = 2048,
            TopP = 0.9f,
            TopK = 40
        };
    }

    /// <summary>
    /// Ask a question about content from a web page.
    /// </summary>
    public async Task<string> AskAboutWebPage(string url, string question)
    {
        // First, scrape the web page
        var webContent = await _webScraper.ScrapeAsLlmContextAsync(url, maxLength: 8000);

        // Create an enhanced prompt with the web content
        var prompt = $@"
I've provided you with content from a web page. Please answer the following question based on this content.

{webContent}

Question: {question}

Please provide a detailed answer based on the web content above.
";

        // Send to the LLM with default settings
        return await _chatService.SendMessageAsync(
            prompt,
            ragMode: false,  // Don't use RAG since we're providing the context directly
            debugMode: false,
            showPerformanceMetrics: false,
            _defaultSettings);
    }

    /// <summary>
    /// Summarize a web page.
    /// </summary>
    public async Task<string> SummarizeWebPage(string url)
    {
        var result = await _webScraper.ScrapeAsync(url);

        if (!result.Success)
        {
            return $"Failed to scrape the page: {result.ErrorMessage}";
        }

        var prompt = $@"
Please provide a comprehensive summary of the following web page content.

Title: {result.Title}
URL: {result.Url}

Content:
{result.TextContent.Substring(0, Math.Min(result.TextContent.Length, 8000))}

Provide a summary that includes:
1. Main topic/purpose
2. Key points (3-5 bullet points)
3. Important details or conclusions
";

        return await _chatService.SendMessageAsync(
            prompt,
            ragMode: false,
            debugMode: false,
            showPerformanceMetrics: false,
            _defaultSettings);
    }

    /// <summary>
    /// Extract structured information from a web page.
    /// </summary>
    public async Task<string> ExtractStructuredInfo(string url, string extractionRequest)
    {
        var webContent = await _webScraper.ScrapeAsLlmContextAsync(url, maxLength: 10000);

        var prompt = $@"
Extract the following information from the web page content provided below.

Extraction Request: {extractionRequest}

{webContent}

Please provide the extracted information in a clear, structured format.
";

        return await _chatService.SendMessageAsync(
            prompt,
            ragMode: false,
            debugMode: false,
            showPerformanceMetrics: false,
            _defaultSettings);
    }

    /// <summary>
    /// Compare content from multiple web pages.
    /// </summary>
    public async Task<string> CompareWebPages(List<string> urls, string comparisonCriteria)
    {
        var scrapeTasks = urls.Select(url => _webScraper.ScrapeAsync(url));
        var results = await Task.WhenAll(scrapeTasks);

        var successfulResults = results.Where(r => r.Success).ToList();
        
        if (!successfulResults.Any())
        {
            return "Failed to scrape any of the provided URLs.";
        }

        var contentBuilder = new System.Text.StringBuilder();
        contentBuilder.AppendLine($"I'm comparing {successfulResults.Count} web pages based on: {comparisonCriteria}");
        contentBuilder.AppendLine();

        for (int i = 0; i < successfulResults.Count; i++)
        {
            var result = successfulResults[i];
            contentBuilder.AppendLine($"=== Source {i + 1}: {result.Title} ===");
            contentBuilder.AppendLine($"URL: {result.Url}");
            contentBuilder.AppendLine();
            
            var content = result.TextContent.Length > 3000 
                ? result.TextContent.Substring(0, 3000) + "..." 
                : result.TextContent;
            
            contentBuilder.AppendLine(content);
            contentBuilder.AppendLine();
        }

        var prompt = $@"
{contentBuilder}

Comparison Criteria: {comparisonCriteria}

Please provide a detailed comparison of these web pages based on the criteria provided.
";

        return await _chatService.SendMessageAsync(
            prompt,
            ragMode: false,
            debugMode: false,
            showPerformanceMetrics: false,
            _defaultSettings);
    }

    /// <summary>
    /// Validate and check if a URL is scrapeable.
    /// </summary>
    public async Task<(bool IsValid, string Message)> ValidateUrl(string url)
    {
        if (!_webScraper.IsValidUrl(url))
        {
            return (false, "Invalid URL format. Please use http:// or https://");
        }

        var result = await _webScraper.ScrapeAsync(url);

        return result.Success
            ? (true, $"Successfully scraped: {result.Title} ({result.TextContent.Length} characters)")
            : (false, $"Failed to scrape: {result.ErrorMessage}");
    }

    /// <summary>
    /// Research a topic by scraping multiple related web pages.
    /// </summary>
    public async Task<string> ResearchTopic(string topic, List<string> sourceUrls)
    {
        var scrapeTasks = sourceUrls.Select(url => 
            _webScraper.ScrapeAsLlmContextAsync(url, maxLength: 4000));
        
        var contexts = await Task.WhenAll(scrapeTasks);

        var combinedContext = string.Join("\n\n---\n\n", contexts);

        var prompt = $@"
I'm researching the topic: {topic}

I've gathered information from {sourceUrls.Count} web sources:

{combinedContext}

Based on these sources, please provide:
1. A comprehensive overview of {topic}
2. Key insights from each source
3. Common themes or agreements across sources
4. Any contradictions or differing viewpoints
5. Conclusions and recommendations

Please cite which source each piece of information comes from.
";

        return await _chatService.SendMessageAsync(
            prompt,
            ragMode: false,
            debugMode: false,
            showPerformanceMetrics: false,
            _defaultSettings);
    }

    /// <summary>
    /// Extract facts or data points from a web page.
    /// </summary>
    public async Task<List<string>> ExtractFacts(string url)
    {
        var result = await _webScraper.ScrapeAsync(url);

        if (!result.Success)
        {
            return new List<string> { $"Error: {result.ErrorMessage}" };
        }

        var prompt = $@"
Extract all important facts, statistics, and data points from the following content.
Present each fact as a separate bullet point.

Title: {result.Title}
Source: {result.Url}

Content:
{result.TextContent.Substring(0, Math.Min(result.TextContent.Length, 8000))}

Please list all facts in the format:
- [Fact/statistic/data point]
";

        var response = await _chatService.SendMessageAsync(
            prompt,
            ragMode: false,
            debugMode: false,
            showPerformanceMetrics: false,
            _defaultSettings);
        
        var facts = response.Split('\n')
            .Where(line => line.Trim().StartsWith("-"))
            .Select(line => line.Trim().TrimStart('-').Trim())
            .ToList();

        return facts;
    }

    /// <summary>
    /// Translate scraped content to another language.
    /// </summary>
    public async Task<string> TranslateWebPage(string url, string targetLanguage)
    {
        var result = await _webScraper.ScrapeAsync(url);

        if (!result.Success)
        {
            return $"Failed to scrape: {result.ErrorMessage}";
        }

        var prompt = $@"
Please translate the following web page content to {targetLanguage}.
Preserve the structure and formatting.

Original Title: {result.Title}
Original URL: {result.Url}

Content to translate:
{result.TextContent.Substring(0, Math.Min(result.TextContent.Length, 6000))}

Provide the translation maintaining the original structure.
";

        return await _chatService.SendMessageAsync(
            prompt,
            ragMode: false,
            debugMode: false,
            showPerformanceMetrics: false,
            _defaultSettings);
    }
}
