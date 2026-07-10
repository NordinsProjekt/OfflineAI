using System.Threading.RateLimiting;
using OfflineAI.Api.Security;
using OfflineAI.Api.Services;
using Application.AI.Pooling;
using Application.AI.Embeddings;
using Application.AI.Gemma4;
using Services.Configuration;
using Services.FileAgent;
using Services.Language;
using Services.Memory;
using Services.Repositories;
using Services.Workspace;
using Infrastructure.Data.Dapper;
using Microsoft.Extensions.AI;
using Microsoft.OpenApi;

// Infrastructure.Data.Dapper uses WindowsIdentity to grant DB access; this app only runs on Windows.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OfflineAI API",
        Version = "v1",
        Description = "REST API for querying local LLM with RAG, workspace file management, and image/PDF support"
    });
});

// Register AppConfiguration
var appConfig = builder.Configuration.GetSection("AppConfiguration").Get<AppConfiguration>() ?? new AppConfiguration();
builder.Services.AddSingleton(appConfig);

// Security settings (API key auth, CORS allow-list, concurrency cap)
var securityOptions = builder.Configuration.GetSection(ApiSecurityOptions.SectionName).Get<ApiSecurityOptions>()
    ?? new ApiSecurityOptions();
builder.Services.AddSingleton(securityOptions);

// Read configuration for LLM - Check both nested object and flat key format
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

// Debug: Print what we found
Console.WriteLine("\n========================================");
Console.WriteLine("?? CONFIGURATION DEBUG");
Console.WriteLine("========================================");
Console.WriteLine($"LLM Executable: {(string.IsNullOrEmpty(llmExe) ? "[NOT SET]" : llmExe)}");
Console.WriteLine($"LLM Model: {(string.IsNullOrEmpty(llmModel) ? "[NOT SET]" : llmModel)}");
Console.WriteLine($"Pool Max Instances: {poolMax}");
Console.WriteLine($"Pool Timeout: {poolTimeout}ms");

// Validate configuration
var configErrors = new List<string>();
if (string.IsNullOrEmpty(llmExe))
{
    configErrors.Add("AppConfiguration:Llm:ExecutablePath is missing");
}
else if (!System.IO.File.Exists(llmExe))
{
    configErrors.Add($"LLM executable not found at: {llmExe}");
}
else
{
    Console.WriteLine($"? Executable exists: {llmExe}");
}

if (string.IsNullOrEmpty(llmModel))
{
    configErrors.Add("AppConfiguration:Llm:ModelPath is missing");
}
else if (!System.IO.File.Exists(llmModel))
{
    configErrors.Add($"LLM model file not found at: {llmModel}");
}
else
{
    Console.WriteLine($"? Model exists: {llmModel}");
}

if (configErrors.Any())
{
    Console.WriteLine("\n========================================");
    Console.WriteLine("??  CONFIGURATION ERRORS DETECTED");
    Console.WriteLine("========================================");
    foreach (var error in configErrors)
    {
        Console.WriteLine($"? {error}");
    }
    Console.WriteLine("\n?? To fix this:");
    Console.WriteLine("   1. Right-click the OfflineAI.Api project");
    Console.WriteLine("   2. Select 'Manage User Secrets'");
    Console.WriteLine("   3. Add the following configuration:\n");
    Console.WriteLine("   {");
    Console.WriteLine("     \"AppConfiguration:Llm:ExecutablePath\": \"C:\\\\path\\\\to\\\\llama-cli.exe\",");
    Console.WriteLine("     \"AppConfiguration:Llm:ModelPath\": \"C:\\\\path\\\\to\\\\model.gguf\"");
    Console.WriteLine("   }\n");
    Console.WriteLine("??  API will start but all query requests will fail.");
    Console.WriteLine("========================================\n");
}

// Register Model Pool
builder.Services.AddSingleton<IModelInstancePool>(sp =>
{
    if (configErrors.Any())
    {
        Console.WriteLine("??  Creating dummy model pool due to configuration errors");
        return new ModelInstancePool(string.Empty, string.Empty, 1, poolTimeout);
    }
    Console.WriteLine($"? Creating model pool: Max={poolMax}, Timeout={poolTimeout}ms");
    return new ModelInstancePool(llmExe, llmModel, poolMax, poolTimeout);
});

// Register LLM Query Service
builder.Services.AddScoped<ILlmQueryService, LlmQueryService>();

// ── RAG (auto vector search): embedding service + vector/domain repositories ──────────────

// Register language stop words service (used to clean queries before vector search)
builder.Services.AddSingleton<ILanguageStopWordsService, LanguageStopWordsService>();

var embeddingModelPath = appConfig.Embedding?.ModelPath ?? builder.Configuration["AppConfiguration:Embedding:ModelPath"] ?? string.Empty;
var embeddingVocabPath = appConfig.Embedding?.VocabPath ?? builder.Configuration["AppConfiguration:Embedding:VocabPath"] ?? string.Empty;
var embeddingDimension = appConfig.Embedding?.Dimension ?? (int.TryParse(builder.Configuration["AppConfiguration:Embedding:Dimension"], out var dim) ? dim : 768);

var embeddingServiceRegistered = false;
if (!string.IsNullOrEmpty(embeddingModelPath) && !string.IsNullOrEmpty(embeddingVocabPath))
{
    try
    {
        builder.Services.AddSingleton(sp => new SemanticEmbeddingService(
            embeddingModelPath,
            embeddingVocabPath,
            embeddingDimension,
            debugMode: false));

        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            sp.GetRequiredService<SemanticEmbeddingService>());

        embeddingServiceRegistered = true;
        Console.WriteLine("[+] Embedding service registered (auto vector search RAG available)");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[!] Warning: Failed to register embedding service: {ex.Message}");
        Console.WriteLine("   Auto vector search RAG will not be available (manual context RAG still works).");
    }
}
else
{
    Console.WriteLine("[!] Embedding service not configured (AppConfiguration:Embedding:ModelPath/VocabPath missing)");
    Console.WriteLine("   Auto vector search RAG disabled; manual context RAG (Context field) still works.");
}

var dbConnectionString = appConfig.Database?.ConnectionString
    ?? builder.Configuration["AppConfiguration:Database:ConnectionString"]
    ?? string.Empty;
var dbTableName = appConfig.Database?.ActiveTableName
    ?? builder.Configuration["AppConfiguration:Database:ActiveTableName"]
    ?? "MemoryFragments";

if (!string.IsNullOrEmpty(dbConnectionString))
{
    try
    {
        builder.Services.AddDapperVectorMemoryRepository(dbConnectionString, dbTableName);
        builder.Services.AddDapperKnowledgeDomainRepository(dbConnectionString);

        Console.WriteLine("[+] Vector memory + knowledge domain repositories registered");

        if (embeddingServiceRegistered)
        {
            builder.Services.AddSingleton<VectorMemoryPersistenceService>();
            Console.WriteLine("[+] Persistence service registered (PDF ingestion into RAG available)");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[!] Warning: Failed to register database services: {ex.Message}");
        Console.WriteLine("   Auto vector search RAG and PDF ingestion will not be available.");
    }
}
else
{
    Console.WriteLine("[!] Database not configured (AppConfiguration:Database:ConnectionString missing)");
    Console.WriteLine("   Auto vector search RAG, domain filtering, and PDF ingestion disabled.");
}

// ── Workspace + file agent: confines uploaded/created files to a user-selected directory ──

builder.Services.AddSingleton<IWorkspaceService>(_ =>
{
    var defaultAgentDir = !string.IsNullOrWhiteSpace(appConfig.Folders.AgentFilesFolder)
        ? appConfig.Folders.AgentFilesFolder
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "OfflineAI", "AgentFiles");
    // Confine every workspace to the configured root (defaults to the parent of the agent-files
    // folder) so an API caller can't point the file agent at an arbitrary location on disk.
    return new WorkspaceService(defaultAgentDir, workspaceRoot: appConfig.Folders.WorkspaceRoot);
});

builder.Services.AddSingleton<IFileAgentService>(sp =>
{
    var workspaceService = sp.GetRequiredService<IWorkspaceService>();
    var fileAgent = new FileAgentService(workspaceService.GetActiveWorkspace().Path);
    workspaceService.ActiveWorkspaceChanged += workspace => fileAgent.SetBaseDirectory(workspace.Path);
    return fileAgent;
});

Console.WriteLine("[+] Workspace + file agent services registered");

// ── Gemma 4 multimodal CLI service: powers image (picture) queries ────────────────────────

var gemma4CliCfg = appConfig.Gemma4Cli;
var gemma4CliExe = !string.IsNullOrWhiteSpace(gemma4CliCfg.LlamaCliPath)
    ? gemma4CliCfg.LlamaCliPath
    : appConfig.Llm?.ExecutablePath ?? string.Empty;
if (!string.IsNullOrWhiteSpace(gemma4CliCfg.ModelPath) && !string.IsNullOrWhiteSpace(gemma4CliExe))
{
    builder.Services.AddSingleton<IGemma4CliService>(sp =>
    {
        var opts = new Gemma4CliOptions
        {
            LlamaCliPath          = gemma4CliExe,
            ModelPath             = gemma4CliCfg.ModelPath,
            GpuLayers             = gemma4CliCfg.GpuLayers,
            ContextSize           = gemma4CliCfg.ContextSize,
            MaxTokens             = gemma4CliCfg.MaxTokens,
            Temperature           = gemma4CliCfg.Temperature,
            TopP                  = gemma4CliCfg.TopP,
            TopK                  = gemma4CliCfg.TopK,
            TimeoutMs             = gemma4CliCfg.TimeoutMs,
            PauseTimeoutMs        = gemma4CliCfg.PauseTimeoutMs,
            MaxToolCallIterations = gemma4CliCfg.MaxToolCallIterations
        };
        Console.WriteLine($"[+] Gemma 4 CLI service registered (model: {Path.GetFileName(gemma4CliCfg.ModelPath)}, image queries available)");
        return new Gemma4CliService(opts);
    });
}
else
{
    Console.WriteLine("[!] Gemma 4 CLI service not configured (AppConfiguration:Gemma4Cli:ModelPath missing)");
    Console.WriteLine("   Image (picture) queries will not be available.");
}

// Configure CORS from an explicit allow-list. Reflecting any origin together with
// AllowCredentials() would let any website the user visits make credentialed calls to this API,
// so origins must be named explicitly. When none are configured, no cross-origin browser access
// is granted (non-browser clients such as scripts are unaffected by CORS).
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

// Bound concurrency so a caller cannot exhaust the (small) model pool. Requests beyond the limit
// queue briefly, then receive HTTP 429 rather than piling up.
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

// Initialize the model pool AFTER the app is built
if (configErrors.Count == 0)
{
    Console.WriteLine("\n========================================");
    Console.WriteLine("?? INITIALIZING MODEL POOL");
    Console.WriteLine("========================================");
    Console.WriteLine($"Loading {poolMax} model instance(s)...");
    Console.WriteLine("This may take 10-30 seconds depending on model size and GPU usage.");
    Console.WriteLine("");

    try
    {
        // Get the pool from DI
        var modelPool = app.Services.GetRequiredService<IModelInstancePool>() as ModelInstancePool;

        if (modelPool != null)
        {
            await modelPool.InitializeAsync((current, total) =>
            {
                Console.WriteLine($"?? [{current}/{total}] Loading model instance {current}...");
            });

            Console.WriteLine($"\n? Model pool initialized: {modelPool.AvailableCount}/{modelPool.MaxInstances} instances ready");
        }
        else
        {
            Console.WriteLine("??  Could not get ModelInstancePool from DI container");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n? Failed to initialize model pool");
        Console.WriteLine($"   Error: {ex.Message}");
        Console.WriteLine($"   Type: {ex.GetType().Name}");
        if (ex.InnerException != null)
        {
            Console.WriteLine($"   Inner: {ex.InnerException.Message}");
        }
        Console.WriteLine("\n??  API will start but query requests will fail until pool is initialized.");
        Console.WriteLine("   The pool will attempt lazy initialization on first request.");
    }
    Console.WriteLine("========================================\n");
}

// Initialize the RAG database on startup (non-blocking)
if (app.Services.GetService<IVectorMemoryRepository>() != null)
{
    _ = Task.Run(async () =>
    {
        try
        {
            var repository = app.Services.GetRequiredService<IVectorMemoryRepository>();
            await repository.InitializeDatabaseAsync();
            Console.WriteLine("[+] RAG database initialized");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] Warning: Failed to initialize RAG database: {ex.Message}");
        }
    });
}

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "OfflineAI API v1");
    c.RoutePrefix = "swagger";
});

// IMPORTANT: CORS must come before UseHttpsRedirection to handle preflight requests
app.UseCors(CorsPolicyName);

// Only use HTTPS redirection if the app is configured to use HTTPS
if (app.Environment.IsDevelopment())
{
    // In development, don't force HTTPS redirection to allow HTTP access from network.
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

// API-key authentication gate (before authorization / controllers). Swagger, health, and CORS
// preflight are allowed through anonymously inside the middleware.
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();
app.MapControllers();

if (securityOptions.RequireApiKey && string.IsNullOrEmpty(securityOptions.ApiKey))
{
    Console.WriteLine("[!] SECURITY: API key auth is ON but no Security:ApiKey is set — all API requests will be rejected until you configure one (user secrets) or set Security:RequireApiKey=false.");
}
else if (!securityOptions.RequireApiKey)
{
    Console.WriteLine("[!] SECURITY: API key auth is DISABLED (Security:RequireApiKey=false). Only run this way on a trusted, localhost-only machine.");
}

Console.WriteLine("========================================");
Console.WriteLine("? OfflineAI API is running");
Console.WriteLine($"?? Swagger UI (HTTPS): https://localhost:7015/swagger");
Console.WriteLine($"?? Swagger UI (HTTP): http://localhost:5118/swagger");
Console.WriteLine($"?? Network Access (HTTP): http://<your-ip>:5118/swagger");
Console.WriteLine($"?? LLM Configured: {!configErrors.Any()}");

// Get final pool status
try
{
    var finalPool = app.Services.GetRequiredService<IModelInstancePool>() as ModelInstancePool;
    if (finalPool != null)
    {
        Console.WriteLine($"?? Model Pool: {finalPool.AvailableCount}/{finalPool.MaxInstances} instances available");
    }
}
catch
{
    // Ignore if pool can't be retrieved
}

Console.WriteLine("========================================\n");

await app.RunAsync();
