using OfflineAI.Api.Services;
using Application.AI.Pooling;
using Services.Configuration;
using Microsoft.OpenApi.Models;

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
        Description = "REST API for querying local LLM with RAG support"
    });
});

// Register AppConfiguration
var appConfig = builder.Configuration.GetSection("AppConfiguration").Get<AppConfiguration>() ?? new AppConfiguration();
builder.Services.AddSingleton(appConfig);

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

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
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

// Configure the HTTP request pipeline
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "OfflineAI API v1");
    c.RoutePrefix = "swagger";
});

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();
app.MapControllers();

Console.WriteLine("========================================");
Console.WriteLine("? OfflineAI API is running");
Console.WriteLine($"?? Swagger UI: https://localhost:7015/swagger");
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

app.Run();
