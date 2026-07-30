using System.Threading.RateLimiting;
using AgentKit.Api.Security;
using AgentKit.Api.Services;
using AgentKit.Skills.External;
using AgentKit.Skills.Utility;
using Application.AI.Pooling;
using Infrastructure.Data.Dapper;
using Services.Configuration;
using Microsoft.OpenApi;

// Infrastructure.Data.Dapper uses WindowsIdentity to grant DB access; this app only runs on Windows.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AgentKit API",
        Version = "v1",
        Description = "Headless goal-agent job API: describe a desired workspace end result, poll progress, and download the resulting files."
    });
});

var appConfig = builder.Configuration.GetSection("AppConfiguration").Get<AppConfiguration>() ?? new AppConfiguration();
builder.Services.AddSingleton(appConfig);

var securityOptions = builder.Configuration.GetSection(ApiSecurityOptions.SectionName).Get<ApiSecurityOptions>()
    ?? new ApiSecurityOptions();
builder.Services.AddSingleton(securityOptions);

var llmExe = appConfig.Llm?.ExecutablePath
    ?? builder.Configuration["AppConfiguration:Llm:ExecutablePath"]
    ?? string.Empty;
var llmModel = appConfig.Llm?.ModelPath
    ?? builder.Configuration["AppConfiguration:Llm:ModelPath"]
    ?? string.Empty;
var poolMax = appConfig.Pool?.MaxInstances
    ?? (int.TryParse(builder.Configuration["AppConfiguration:Pool:MaxInstances"], out var m) ? m : 3);
var poolTimeout = appConfig.Pool?.TimeoutMs
    ?? (int.TryParse(builder.Configuration["AppConfiguration:Pool:TimeoutMs"], out var t) ? t : 300000);

Console.WriteLine("========================================");
Console.WriteLine("AgentKit API - configuration");
Console.WriteLine("========================================");
Console.WriteLine($"LLM Executable: {(string.IsNullOrEmpty(llmExe) ? "[NOT SET]" : llmExe)}");
Console.WriteLine($"LLM Model: {(string.IsNullOrEmpty(llmModel) ? "[NOT SET]" : llmModel)}");
Console.WriteLine($"Pool Max Instances: {poolMax}");

var configErrors = new List<string>();
if (string.IsNullOrEmpty(llmExe))
    configErrors.Add("AppConfiguration:Llm:ExecutablePath is missing");
else if (!File.Exists(llmExe))
    configErrors.Add($"LLM executable not found at: {llmExe}");

if (string.IsNullOrEmpty(llmModel))
    configErrors.Add("AppConfiguration:Llm:ModelPath is missing");
else if (!File.Exists(llmModel))
    configErrors.Add($"LLM model file not found at: {llmModel}");

if (configErrors.Count > 0)
{
    Console.WriteLine("[!] Configuration errors:");
    foreach (var error in configErrors)
        Console.WriteLine($"    - {error}");
    Console.WriteLine("[!] API will start but job requests will fail until this is configured (user secrets).");
}

// Register model pool — the actual pooled LLM subprocess manager. A dummy pool (1 empty
// instance) is used when misconfigured, so the API still starts and fails job requests with a
// clear error instead of a startup crash.
builder.Services.AddSingleton<IModelInstancePool>(_ =>
    configErrors.Count > 0
        ? new ModelInstancePool(string.Empty, string.Empty, 1, poolTimeout)
        : new ModelInstancePool(llmExe, llmModel, poolMax, poolTimeout));

// ── Agent tools offered to jobs: HTTP endpoints + external executables the LLM may call by
//    name, resolved only from AppConfiguration.AgentTools — the LLM never supplies a raw
//    URL/path. Shared across jobs since neither service holds per-workspace state (unlike the
//    file agent, which each job gets its own instance of). ──
builder.Services.AddHttpClient("AgentApiTools");
builder.Services.AddSingleton<IUtilityToolsService>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    return new UtilityToolsService(
        MapUtilityToolsOptions(appConfig.AgentTools),
        () => httpClientFactory.CreateClient("AgentApiTools"));
});
builder.Services.AddSingleton<IExternalToolsService>(_ =>
    new ExternalToolsService(MapExternalToolOptions(appConfig.AgentTools.ExternalTools)));

// ── Run history: optional. Enables the database status fallback (job survives a server
//    restart, at least as far as its persisted record) and the recent-jobs list. Jobs still run
//    fine without it — they're just tracked in memory only, and vanish from GET endpoints once
//    the process restarts. ──
var dbConnectionString = appConfig.Database?.ConnectionString
    ?? builder.Configuration["AppConfiguration:Database:ConnectionString"]
    ?? string.Empty;
if (!string.IsNullOrEmpty(dbConnectionString))
{
    try
    {
        builder.Services.AddDapperAgentRunRepository(dbConnectionString);
        Console.WriteLine("[+] Agent run repository registered (job history + restart-safe status available)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[!] Failed to register agent run repository: {ex.Message}");
    }
}
else
{
    Console.WriteLine("[!] Database not configured (AppConfiguration:Database:ConnectionString missing) — job history and restart-safe status disabled.");
}

// ── Peer clustering: forward a job to another AgentKit.Api instance when this node is too busy
//    and a peer (AppConfiguration.Cluster.Peers) has room. A short timeout keeps a dead/slow
//    peer from stalling job submission. ──
builder.Services.AddHttpClient(ClusterPeerClient.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddSingleton<IClusterPeerClient, ClusterPeerClient>();

builder.Services.AddSingleton<IAgentJobService, AgentJobService>();

// Configure CORS from an explicit allow-list — see OfflineAI.Api/Program.cs for the same
// reasoning (never reflect arbitrary origins together with credentials).
const string CorsPolicyName = "ConfiguredOrigins";
var allowedOrigins = securityOptions.AllowedCorsOrigins
    .Where(o => !string.IsNullOrWhiteSpace(o))
    .ToArray();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        // else: empty policy — cross-origin browser requests are denied.
    });
});

var maxConcurrent = securityOptions.MaxConcurrentRequests > 0
    ? securityOptions.MaxConcurrentRequests
    : Math.Max(2, poolMax * 2);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("global", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = maxConcurrent,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = maxConcurrent * 2
        }));
});

var app = builder.Build();

if (configErrors.Count == 0)
{
    try
    {
        if (app.Services.GetRequiredService<IModelInstancePool>() is ModelInstancePool modelPool)
        {
            await modelPool.InitializeAsync((current, total) =>
                Console.WriteLine($"Loading model instance {current}/{total}..."));
            Console.WriteLine($"[+] Model pool initialized: {modelPool.AvailableCount}/{modelPool.MaxInstances} instances ready");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[!] Failed to initialize model pool: {ex.Message}");
        Console.WriteLine("    API will start but job requests will fail until the pool is initialized.");
    }
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AgentKit API v1");
    c.RoutePrefix = "swagger";
});

// IMPORTANT: CORS must come before UseHttpsRedirection to handle preflight requests.
app.UseCors(CorsPolicyName);

if (app.Environment.IsDevelopment())
{
    // In development, don't force HTTPS redirection to allow HTTP access from the LAN.
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

// API-key authentication gate (before authorization/controllers). Swagger, health, and CORS
// preflight are allowed through anonymously inside the middleware.
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();
app.MapControllers();

if (securityOptions.RequireApiKey && string.IsNullOrEmpty(securityOptions.ApiKey))
{
    Console.WriteLine("[!] SECURITY: API key auth is ON but no Security:ApiKey is set — all requests will be rejected until you configure one (user secrets) or set Security:RequireApiKey=false.");
}
else if (!securityOptions.RequireApiKey)
{
    Console.WriteLine("[!] SECURITY: API key auth is DISABLED (Security:RequireApiKey=false). Only run this way on a trusted, LAN-only machine.");
}

Console.WriteLine("========================================");
Console.WriteLine("AgentKit API is running");
Console.WriteLine("========================================");

await app.RunAsync();

/// <summary>Maps the host's utility-tool endpoint whitelist to AgentKit's <see cref="UtilityToolsOptions"/>.</summary>
static UtilityToolsOptions MapUtilityToolsOptions(AgentToolsSettings agentTools) => new()
{
    Endpoints = agentTools.Endpoints.Select(e => new ApiEndpointOptions
    {
        Name = e.Name,
        Description = e.Description,
        Url = e.Url,
        Method = e.Method,
        Headers = new Dictionary<string, string>(e.Headers),
        TimeoutMs = e.TimeoutMs,
        MaxResponseLength = e.MaxResponseLength
    }).ToList()
};

/// <summary>Maps the host's external-tool whitelist to AgentKit's <see cref="ExternalToolOptions"/> list.</summary>
static List<ExternalToolOptions> MapExternalToolOptions(IEnumerable<ExternalToolSettings> tools) =>
    tools.Select(t => new ExternalToolOptions
    {
        Command = t.Command,
        ExecutablePath = t.ExecutablePath,
        Description = t.Description,
        Usage = t.Usage,
        FixedArguments = t.FixedArguments,
        TimeoutMs = t.TimeoutMs,
        MaxOutputLength = t.MaxOutputLength
    }).ToList();
