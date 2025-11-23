using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.Data.Dapper;
using Services.Memory;
using Services.Repositories;

namespace Tools;

/// <summary>
/// Console tool for cleaning memory fragments in the database.
/// Removes EOS, EOF, special tokens, and control characters from existing fragments.
/// 
/// Usage:
///   dotnet run --project Tools scan --collection "MyCollection"
///   dotnet run --project Tools clean --collection "MyCollection" --dry-run
///   dotnet run --project Tools clean --collection "MyCollection"
///   dotnet run --project Tools stats
/// </summary>
public class MemoryCleanerTool
{
    public static async Task<int> Main(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        
        // Setup DI
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);
        var serviceProvider = services.BuildServiceProvider();
        
        // Create root command
        var rootCommand = new RootCommand("Memory Fragment Cleaning Tool");
        
        // Scan command
        var scanCommand = new Command("scan", "Scan a collection for fragments that need cleaning");
        var scanCollectionOption = new Option<string>("--collection", "Collection name to scan") { IsRequired = true };
        scanCommand.AddOption(scanCollectionOption);
        scanCommand.SetHandler(async (string collection) =>
        {
            var service = serviceProvider.GetRequiredService<MemoryFragmentCleaningService>();
            await ScanCollection(service, collection);
        }, scanCollectionOption);
        rootCommand.AddCommand(scanCommand);
        
        // Clean command
        var cleanCommand = new Command("clean", "Clean a collection by removing special tokens and control characters");
        var cleanCollectionOption = new Option<string>("--collection", "Collection name to clean") { IsRequired = true };
        var dryRunOption = new Option<bool>("--dry-run", "Simulate cleaning without making changes");
        cleanCommand.AddOption(cleanCollectionOption);
        cleanCommand.AddOption(dryRunOption);
        cleanCommand.SetHandler(async (string collection, bool dryRun) =>
        {
            var service = serviceProvider.GetRequiredService<MemoryFragmentCleaningService>();
            await CleanCollection(service, collection, dryRun);
        }, cleanCollectionOption, dryRunOption);
        rootCommand.AddCommand(cleanCommand);
        
        // Stats command
        var statsCommand = new Command("stats", "Show statistics for all collections");
        statsCommand.SetHandler(async () =>
        {
            var service = serviceProvider.GetRequiredService<MemoryFragmentCleaningService>();
            await ShowStats(service);
        });
        rootCommand.AddCommand(statsCommand);
        
        // Invoke command
        return await rootCommand.InvokeAsync(args);
    }
    
    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        // Get connection string
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found");
        
        // Register repositories
        services.AddSingleton<IVectorMemoryRepository>(sp => 
            new VectorMemoryRepository(connectionString));
        
        // Register cleaning service
        services.AddSingleton<MemoryFragmentCleaningService>();
    }
    
    private static async Task ScanCollection(MemoryFragmentCleaningService service, string collection)
    {
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine($"  SCANNING COLLECTION: {collection}");
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine();
        
        var result = await service.ScanCollectionAsync(collection);
        
        Console.WriteLine();
        Console.WriteLine("SCAN RESULTS");
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine($"Total fragments:              {result.TotalFragments}");
        Console.WriteLine($"Fragments with content issues: {result.ContentIssuesFound}");
        Console.WriteLine($"Fragments with category issues: {result.CategoryIssuesFound}");
        Console.WriteLine($"Issue percentage:             {result.IssuePercentage:F2}%");
        Console.WriteLine();
        
        if (result.FragmentsNeedingCleanup.Any())
        {
            Console.WriteLine("SAMPLE ISSUES (first 10):");
            Console.WriteLine("???????????????????????????????????????????????????????");
            foreach (var issue in result.FragmentsNeedingCleanup.Take(10))
            {
                Console.WriteLine($"[{issue.FragmentId}] {issue.Category}");
                Console.WriteLine($"  Content length: {issue.ContentLength} chars");
                Console.WriteLine($"  Issues: {issue.Report}");
                Console.WriteLine();
            }
            
            if (result.FragmentsNeedingCleanup.Count > 10)
            {
                Console.WriteLine($"... and {result.FragmentsNeedingCleanup.Count - 10} more");
            }
        }
        else
        {
            Console.WriteLine("? No issues found! Collection is clean.");
        }
        
        Console.WriteLine();
        Console.WriteLine("???????????????????????????????????????????????????????");
    }
    
    private static async Task CleanCollection(MemoryFragmentCleaningService service, string collection, bool dryRun)
    {
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine($"  {(dryRun ? "DRY RUN - " : "")}CLEANING COLLECTION: {collection}");
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine();
        
        if (dryRun)
        {
            Console.WriteLine("??  DRY RUN MODE - No changes will be made");
            Console.WriteLine();
        }
        
        var result = await service.CleanCollectionAsync(collection, dryRun);
        
        Console.WriteLine();
        Console.WriteLine("CLEANING RESULTS");
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine($"Total fragments:      {result.TotalFragments}");
        Console.WriteLine($"Fragments updated:    {result.FragmentsUpdated}");
        Console.WriteLine($"Categories cleaned:   {result.CategoriesCleaned}");
        Console.WriteLine($"Contents cleaned:     {result.ContentsCleaned}");
        Console.WriteLine();
        
        if (result.SignificantChanges.Any())
        {
            Console.WriteLine("SIGNIFICANT CHANGES (>10 characters removed):");
            Console.WriteLine("???????????????????????????????????????????????????????");
            foreach (var change in result.SignificantChanges)
            {
                Console.WriteLine($"[{change.FragmentId}] {change.Category}");
                Console.WriteLine($"  {change.OriginalLength} ? {change.CleanedLength} chars ({change.CharactersRemoved} removed)");
                Console.WriteLine();
            }
        }
        
        if (dryRun && result.FragmentsUpdated > 0)
        {
            Console.WriteLine();
            Console.WriteLine("To apply these changes, run without --dry-run:");
            Console.WriteLine($"  dotnet run --project Tools clean --collection \"{collection}\"");
        }
        else if (!dryRun && result.FragmentsUpdated > 0)
        {
            Console.WriteLine();
            Console.WriteLine("? Collection cleaned successfully!");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("? No changes needed - collection is already clean!");
        }
        
        Console.WriteLine();
        Console.WriteLine("???????????????????????????????????????????????????????");
    }
    
    private static async Task ShowStats(MemoryFragmentCleaningService service)
    {
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine("  ALL COLLECTIONS STATISTICS");
        Console.WriteLine("???????????????????????????????????????????????????????");
        Console.WriteLine();
        
        var stats = await service.GetAllCollectionStatsAsync();
        
        if (!stats.Any())
        {
            Console.WriteLine("No collections found.");
            return;
        }
        
        Console.WriteLine($"{"Collection",-40} {"Fragments",10} {"Issues",10} {"Percent",10}");
        Console.WriteLine("".PadRight(70, '?'));
        
        foreach (var stat in stats)
        {
            var issueIndicator = stat.IssuePercentage > 5 ? "?? " : stat.IssuePercentage > 0 ? "?  " : "? ";
            Console.WriteLine($"{issueIndicator}{stat.CollectionName,-38} {stat.TotalFragments,10} {stat.FragmentsWithIssues,10} {stat.IssuePercentage,9:F2}%");
        }
        
        Console.WriteLine();
        Console.WriteLine($"Total collections: {stats.Count}");
        Console.WriteLine($"Collections with issues: {stats.Count(s => s.FragmentsWithIssues > 0)}");
        Console.WriteLine();
        Console.WriteLine("???????????????????????????????????????????????????????");
    }
}
