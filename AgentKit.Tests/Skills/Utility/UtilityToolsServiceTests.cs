using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AgentKit.Skills.Utility;
using FluentAssertions;

namespace AgentKit.Tests.Skills.Utility;

/// <summary>
/// Unit tests for <see cref="UtilityToolsService"/>: the built-in /tid, /datum, and /api
/// utility commands. HTTP calls are exercised against a fake <see cref="HttpMessageHandler"/> so
/// no real network access happens; endpoint scoping is verified so the LLM can only reach
/// destinations explicitly configured in <see cref="UtilityToolsOptions.Endpoints"/>.
/// </summary>
public class UtilityToolsServiceTests
{
    // ── Test doubles ─────────────────────────────────────────────────────

    /// <summary>
    /// Fake handler that answers HTTP requests via a caller-supplied responder and records the
    /// last request sent, so tests can assert on the URL/method/headers actually issued.
    /// </summary>
    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return _responder(request, cancellationToken);
        }
    }

    /// <summary>
    /// Fake factory that hands out a new <see cref="HttpClient"/> per call (mirroring real
    /// <c>IHttpClientFactory</c> semantics) backed by the same shared fake handler, so
    /// <c>UtilityToolsService</c> can freely set <see cref="HttpClient.Timeout"/> on each call.
    /// Pass <see cref="CreateClient"/> as the service's <c>Func&lt;HttpClient&gt;</c> provider.
    /// </summary>
    private sealed class FakeHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient() => new(_handler, disposeHandler: false);
    }

    private static UtilityToolsService CreateSut(
        UtilityToolsOptions? options = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder = null)
    {
        options ??= new UtilityToolsOptions();
        responder ??= (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) });
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler(responder));
        return new UtilityToolsService(options, factory.CreateClient);
    }

    private static UtilityToolsOptions CreateOptionsWithEndpoint(ApiEndpointOptions endpoint) =>
        new() { Endpoints = new List<ApiEndpointOptions> { endpoint } };

    // ── Constructor guards ───────────────────────────────────────────────

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var factory = new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage())));

        var act = () => new UtilityToolsService(null!, factory.CreateClient);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullHttpClientProvider_ThrowsArgumentNullException()
    {
        var act = () => new UtilityToolsService(new UtilityToolsOptions(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── IsCommand ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/tid")]
    [InlineData("  /tid")]
    [InlineData("/TID")]
    [InlineData("/datum")]
    [InlineData("/DATUM")]
    [InlineData("/api weather Hur är vädret?")]
    public void IsCommand_RecognisedCommand_ReturnsTrue(string input)
    {
        var sut = CreateSut();

        sut.IsCommand(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/lista")]
    [InlineData("hej")]
    [InlineData("/apid weather")]
    [InlineData("/tid  ")] // trailing whitespace is not trimmed by IsCommand, only leading
    public void IsCommand_UnrecognisedInput_ReturnsFalse(string input)
    {
        var sut = CreateSut();

        sut.IsCommand(input).Should().BeFalse();
    }

    // ── ExecuteAsync: /tid, /datum ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyInput_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.ExecuteAsync("   ");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Tomt kommando");
    }

    [Fact]
    public async Task ExecuteAsync_TidCommand_ReturnsCurrentTime()
    {
        var sut = CreateSut();

        var result = await sut.ExecuteAsync("/tid");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("Klockan är nu");
    }

    [Fact]
    public async Task ExecuteAsync_DatumCommand_ReturnsCurrentDate()
    {
        var sut = CreateSut();

        var result = await sut.ExecuteAsync("/datum");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain("Dagens datum är");
    }

    [Fact]
    public async Task ExecuteAsync_UnknownCommand_ReturnsFailure()
    {
        var sut = CreateSut();

        var result = await sut.ExecuteAsync("/okänd");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Okänt kommando");
    }

    // ── ExecuteAsync: /api round trip ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ApiCommand_ParsesEndpointAndInstruction_CallsConfiguredEndpoint()
    {
        var endpoint = new ApiEndpointOptions { Name = "weather", Url = "https://api.example.com/weather?q={input}" };
        var options = CreateOptionsWithEndpoint(endpoint);
        var sut = CreateSut(options, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Soligt, 20 grader") }));

        var result = await sut.ExecuteAsync("/api weather Hur är vädret i Stockholm?");

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("weather");
        result.InjectedContext.Should().Contain("Soligt, 20 grader");
        result.InjectedContext.Should().Contain("Hur är vädret i Stockholm?");
    }

    // ── CallNamedApiAsync: validation ────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CallNamedApiAsync_EmptyEndpointName_ReturnsFailure(string? endpointName)
    {
        var sut = CreateSut();

        var result = await sut.CallNamedApiAsync(endpointName!);

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Ange namnet");
    }

    [Fact]
    public async Task CallNamedApiAsync_NoEndpointsConfigured_ReturnsFailureIndicatingNoneConfigured()
    {
        var sut = CreateSut();

        var result = await sut.CallNamedApiAsync("weather");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Inga slutpunkter är konfigurerade");
    }

    [Fact]
    public async Task CallNamedApiAsync_UnknownEndpoint_ListsAvailableEndpoints()
    {
        var options = CreateOptionsWithEndpoint(new ApiEndpointOptions { Name = "weather", Url = "https://api.example.com" });
        var sut = CreateSut(options);

        var result = await sut.CallNamedApiAsync("news");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("news").And.Contain("weather");
    }

    [Fact]
    public async Task CallNamedApiAsync_EndpointWithEmptyUrl_ReturnsFailure()
    {
        var options = CreateOptionsWithEndpoint(new ApiEndpointOptions { Name = "broken", Url = "" });
        var sut = CreateSut(options);

        var result = await sut.CallNamedApiAsync("broken");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("saknar en konfigurerad URL");
    }

    // ── CallNamedApiAsync: success paths ─────────────────────────────────

    [Fact]
    public async Task CallNamedApiAsync_SuccessfulCall_ReturnsBodyAndInstructionAsInjectedContext()
    {
        var endpoint = new ApiEndpointOptions { Name = "weather", Url = "https://api.example.com/weather" };
        var options = CreateOptionsWithEndpoint(endpoint);
        var sut = CreateSut(options, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("Soligt") }));

        var result = await sut.CallNamedApiAsync("weather", "Hur är vädret?");

        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Be("✓ API anropat: weather");
        result.InjectedContext.Should().Contain("Instruktion: Hur är vädret?");
        result.InjectedContext.Should().Contain("Soligt");
    }

    [Fact]
    public async Task CallNamedApiAsync_UrlContainsInputPlaceholder_SubstitutesEscapedInstruction()
    {
        var endpoint = new ApiEndpointOptions { Name = "weather", Url = "https://api.example.com/weather?q={input}" };
        var options = CreateOptionsWithEndpoint(endpoint);
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }));
        var sut = new UtilityToolsService(options, new FakeHttpClientFactory(handler).CreateClient);

        await sut.CallNamedApiAsync("weather", "Stockholm väder");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Contain(Uri.EscapeDataString("Stockholm väder"));
    }

    [Fact]
    public async Task CallNamedApiAsync_ResponseLongerThanMaxLength_TruncatesBody()
    {
        var endpoint = new ApiEndpointOptions { Name = "big", Url = "https://api.example.com/big", MaxResponseLength = 10 };
        var options = CreateOptionsWithEndpoint(endpoint);
        var longBody = new string('x', 50);
        var sut = CreateSut(options, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(longBody) }));

        var result = await sut.CallNamedApiAsync("big");

        result.IsSuccess.Should().BeTrue();
        result.InjectedContext.Should().Contain(new string('x', 10) + "\n...[trunkerat]");
        result.InjectedContext.Should().NotContain(new string('x', 11));
    }

    [Fact]
    public async Task CallNamedApiAsync_CustomHeadersConfigured_AreSentWithRequest()
    {
        var endpoint = new ApiEndpointOptions
        {
            Name = "secure",
            Url = "https://api.example.com/secure",
            Headers = new Dictionary<string, string> { ["X-Api-Key"] = "secret-123" }
        };
        var options = CreateOptionsWithEndpoint(endpoint);
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }));
        var sut = new UtilityToolsService(options, new FakeHttpClientFactory(handler).CreateClient);

        await sut.CallNamedApiAsync("secure");

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.GetValues("X-Api-Key").Should().ContainSingle().Which.Should().Be("secret-123");
    }

    [Fact]
    public async Task CallNamedApiAsync_ConfiguredMethodPost_UsesPostVerb()
    {
        var endpoint = new ApiEndpointOptions { Name = "poster", Url = "https://api.example.com/post", Method = "POST" };
        var options = CreateOptionsWithEndpoint(endpoint);
        var handler = new FakeHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }));
        var sut = new UtilityToolsService(options, new FakeHttpClientFactory(handler).CreateClient);

        await sut.CallNamedApiAsync("poster");

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
    }

    // ── CallNamedApiAsync: failure paths ──────────────────────────────────

    [Fact]
    public async Task CallNamedApiAsync_NonSuccessStatusCode_ReturnsFailureWithStatusCode()
    {
        var endpoint = new ApiEndpointOptions { Name = "weather", Url = "https://api.example.com/weather" };
        var options = CreateOptionsWithEndpoint(endpoint);
        var sut = CreateSut(options, (_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("error") }));

        var result = await sut.CallNamedApiAsync("weather");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("500");
    }

    [Fact]
    public async Task CallNamedApiAsync_HttpRequestException_ReturnsNetworkErrorFailure()
    {
        var endpoint = new ApiEndpointOptions { Name = "weather", Url = "https://api.example.com/weather" };
        var options = CreateOptionsWithEndpoint(endpoint);
        var sut = CreateSut(options, (_, _) => throw new HttpRequestException("DNS failure"));

        var result = await sut.CallNamedApiAsync("weather");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("Nätverksfel");
    }

    [Fact]
    public async Task CallNamedApiAsync_RequestExceedsTimeout_ReturnsTimeoutFailure()
    {
        var endpoint = new ApiEndpointOptions { Name = "slow", Url = "https://api.example.com/slow", TimeoutMs = 50 };
        var options = CreateOptionsWithEndpoint(endpoint);
        var sut = CreateSut(options, async (_, ct) =>
        {
            await Task.Delay(2000, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var result = await sut.CallNamedApiAsync("slow");

        result.IsSuccess.Should().BeFalse();
        result.Message.Should().Contain("timeout");
    }

    // ── GetApiEndpointNames / GetToolDescriptions ─────────────────────────

    [Fact]
    public void GetApiEndpointNames_ReturnsConfiguredNames()
    {
        var options = new UtilityToolsOptions
        {
            Endpoints = new List<ApiEndpointOptions>
            {
                new() { Name = "weather", Url = "https://a" },
                new() { Name = "news", Url = "https://b" }
            }
        };
        var sut = CreateSut(options);

        sut.GetApiEndpointNames().Should().Equal("weather", "news");
    }

    [Fact]
    public void GetToolDescriptions_NoEndpoints_ReturnsOnlyTimeAndDate()
    {
        var sut = CreateSut();

        var descriptions = sut.GetToolDescriptions();

        descriptions.Should().ContainKey("/tid");
        descriptions.Should().ContainKey("/datum");
        descriptions.Keys.Should().NotContain(k => k.StartsWith("/api"));
    }

    [Fact]
    public void GetToolDescriptions_WithEndpoints_IncludesApiDescriptionListingNames()
    {
        var options = CreateOptionsWithEndpoint(new ApiEndpointOptions { Name = "weather", Url = "https://a" });
        var sut = CreateSut(options);

        var descriptions = sut.GetToolDescriptions();

        var apiKey = descriptions.Keys.Should().ContainSingle(k => k.StartsWith("/api")).Which;
        descriptions[apiKey].Should().Contain("weather");
    }

    // ── TryFindCommand ───────────────────────────────────────────────────

    [Fact]
    public void TryFindCommand_ResponseContainsTidOnItsOwnLine_ReturnsTrueWithCommand()
    {
        var sut = CreateSut();

        var found = sut.TryFindCommand("Jag kollar tiden åt dig.\n/tid\n", out var command);

        found.Should().BeTrue();
        command.Should().Be("/tid");
    }

    [Fact]
    public void TryFindCommand_NoCommandPresent_ReturnsFalse()
    {
        var sut = CreateSut();

        var found = sut.TryFindCommand("Jag vet inte.", out var command);

        found.Should().BeFalse();
        command.Should().BeEmpty();
    }

    [Fact]
    public void TryFindCommand_EmptyResponse_ReturnsFalse()
    {
        var sut = CreateSut();

        var found = sut.TryFindCommand("", out _);

        found.Should().BeFalse();
    }
}
