using Xunit;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AiDashboard.Services;
using Microsoft.Extensions.Logging;

namespace Presentation.AiDashboard.Tests.Services;

/// <summary>
/// Unit tests for the WebScraperService.
/// These tests demonstrate how to test the web scraper with mocked HTTP responses.
/// NOTE: If tests were to use real LLM calls, they should use LlmProgressTracker.ShortTimeoutMs (10 seconds)
/// </summary>
public class WebScraperServiceTests
{
    // For any tests that would use real LLM calls (not these mock tests)
    private const int TestLlmTimeoutMs = LlmProgressTracker.ShortTimeoutMs; // 10 seconds for tests
    
    private Mock<IHttpClientFactory> CreateMockHttpClientFactory(HttpStatusCode statusCode, string content)
    {
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory
            .Setup(f => f.CreateClient("WebScraper"))
            .Returns(httpClient);

        return mockFactory;
    }

    [Fact]
    public void IsValidUrl_ValidHttpUrl_ReturnsTrue()
    {
        // Arrange
        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, "");
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = service.IsValidUrl("http://example.com");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValidUrl_ValidHttpsUrl_ReturnsTrue()
    {
        // Arrange
        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, "");
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = service.IsValidUrl("https://example.com");

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("file:///C:/test.html")]
    public void IsValidUrl_InvalidUrl_ReturnsFalse(string invalidUrl)
    {
        // Arrange
        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, "");
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = service.IsValidUrl(invalidUrl);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ScrapeAsync_ValidUrl_ReturnsSuccessfulResult()
    {
        // Arrange
        var html = @"
            <html>
                <head>
                    <title>Test Page</title>
                    <meta name='description' content='Test description'>
                </head>
                <body>
                    <h1>Main Heading</h1>
                    <p>This is test content.</p>
                    <a href='https://example.com'>Link</a>
                </body>
            </html>";

        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, html);
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = await service.ScrapeAsync("https://example.com");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Test Page", result.Title);
        Assert.Contains("test content", result.TextContent);
        Assert.Contains("H1: Main Heading", result.Headers);
        Assert.Equal(200, result.StatusCode);
    }

    [Fact]
    public async Task ScrapeAsync_NotFoundUrl_ReturnsFailedResult()
    {
        // Arrange
        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.NotFound, "");
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = await service.ScrapeAsync("https://example.com/notfound");

        // Assert
        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task ScrapeAsync_InvalidUrl_ReturnsFailedResult()
    {
        // Arrange
        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, "");
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = await service.ScrapeAsync("not-a-valid-url");

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Invalid URL format", result.ErrorMessage);
    }

    [Fact]
    public async Task ScrapeAsync_ExtractsMetadata()
    {
        // Arrange
        var html = @"
            <html>
                <head>
                    <title>Test</title>
                    <meta name='description' content='Test Description'>
                    <meta name='keywords' content='test, keywords'>
                    <meta property='og:title' content='OG Title'>
                </head>
                <body>Content</body>
            </html>";

        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, html);
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = await service.ScrapeAsync("https://example.com");

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Metadata.ContainsKey("description"));
        Assert.Equal("Test Description", result.Metadata["description"]);
        Assert.True(result.Metadata.ContainsKey("keywords"));
    }

    [Fact]
    public async Task ScrapeAsync_ExtractsHeaders()
    {
        // Arrange
        var html = @"
            <html>
                <body>
                    <h1>Heading 1</h1>
                    <h2>Heading 2</h2>
                    <h3>Heading 3</h3>
                </body>
            </html>";

        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, html);
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = await service.ScrapeAsync("https://example.com");

        // Assert
        Assert.True(result.Success);
        Assert.Contains("H1: Heading 1", result.Headers);
        Assert.Contains("H2: Heading 2", result.Headers);
        Assert.Contains("H3: Heading 3", result.Headers);
    }

    [Fact]
    public async Task ScrapeAsync_FiltersOutScriptsAndStyles()
    {
        // Arrange
        var html = @"
            <html>
                <head>
                    <style>body { color: red; }</style>
                </head>
                <body>
                    <p>Visible content</p>
                    <script>console.log('hidden');</script>
                    <nav>Navigation</nav>
                </body>
            </html>";

        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, html);
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = await service.ScrapeAsync("https://example.com");

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Visible content", result.TextContent);
        Assert.DoesNotContain("color: red", result.TextContent);
        Assert.DoesNotContain("console.log", result.TextContent);
    }

    [Fact]
    public async Task ScrapeAsLlmContextAsync_FormatsCorrectly()
    {
        // Arrange
        var html = @"
            <html>
                <head>
                    <title>Test Article</title>
                    <meta name='description' content='Article description'>
                </head>
                <body>
                    <h1>Main Title</h1>
                    <p>Article content goes here.</p>
                </body>
            </html>";

        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, html);
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var context = await service.ScrapeAsLlmContextAsync("https://example.com");

        // Assert
        Assert.Contains("# Web Content Context", context);
        Assert.Contains("Test Article", context);
        Assert.Contains("Article description", context);
        Assert.Contains("Main Title", context);
        Assert.Contains("Article content", context);
    }

    [Fact]
    public async Task ScrapeAsLlmContextAsync_RespectsMaxLength()
    {
        // Arrange
        var longContent = new string('x', 10000);
        var html = $"<html><body><p>{longContent}</p></body></html>";

        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, html);
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var context = await service.ScrapeAsLlmContextAsync("https://example.com", maxLength: 500);

        // Assert
        Assert.True(context.Length <= 500);
        Assert.Contains("truncated", context);
    }

    [Fact]
    public async Task ScrapeAsLlmContextAsync_FailedScrape_ReturnsErrorMessage()
    {
        // Arrange
        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.NotFound, "");
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var context = await service.ScrapeAsLlmContextAsync("https://example.com");

        // Assert
        Assert.Contains("Web Scraping Error", context);
        Assert.Contains("https://example.com", context);
    }

    [Fact]
    public async Task ScrapeAsync_ExtractsLinks()
    {
        // Arrange
        var html = @"
            <html>
                <body>
                    <a href='https://example.com/page1'>Link 1</a>
                    <a href='/relative'>Relative Link</a>
                    <a href='page2.html'>Page 2</a>
                </body>
            </html>";

        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, html);
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = await service.ScrapeAsync("https://example.com");

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.Links);
        Assert.Contains("https://example.com/page1", result.Links);
    }

    [Fact]
    public async Task ScrapeAsync_NoTitleTag_UsesFallback()
    {
        // Arrange
        var html = @"
            <html>
                <body>
                    <h1>First Heading</h1>
                    <p>Content</p>
                </body>
            </html>";

        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, html);
        var service = new WebScraperService(mockFactory.Object);

        // Act
        var result = await service.ScrapeAsync("https://example.com");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("First Heading", result.Title);
    }

    [Fact]
    public async Task ScrapeAsync_SetsScrapedAtTimestamp()
    {
        // Arrange
        var mockFactory = CreateMockHttpClientFactory(HttpStatusCode.OK, "<html><body>Test</body></html>");
        var service = new WebScraperService(mockFactory.Object);
        var beforeScrape = DateTime.UtcNow;

        // Act
        var result = await service.ScrapeAsync("https://example.com");
        var afterScrape = DateTime.UtcNow;

        // Assert
        Assert.True(result.Success);
        Assert.True(result.ScrapedAt >= beforeScrape);
        Assert.True(result.ScrapedAt <= afterScrape);
    }
}
