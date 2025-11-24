using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Entities;
using Services.Repositories;
using Services.Utilities;

namespace Services.Memory;

/// <summary>
/// Service for cleaning existing memory fragments in the database.
/// Removes EOS, EOF, special tokens, and control characters from content.
/// </summary>
public class MemoryFragmentCleaningService
{
    private readonly IVectorMemoryRepository _repository;
    
    public MemoryFragmentCleaningService(IVectorMemoryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }
    
    /// <summary>
    /// Scan a collection for memory fragments that need cleaning.
    /// </summary>
    public async Task<CleaningScanResult> ScanCollectionAsync(string collectionName)
    {
        Console.WriteLine($"[*] Scanning collection '{collectionName}' for issues...");
        
        var fragments = await _repository.LoadByCollectionAsync(collectionName);
        var result = new CleaningScanResult
        {
            CollectionName = collectionName,
            TotalFragments = fragments.Count
        };
        
        foreach (var fragment in fragments)
        {
            // Check category
            var categoryReport = MemoryFragmentCleaner.AnalyzeText(fragment.Category);
            if (categoryReport.HasIssues)
            {
                result.CategoryIssuesFound++;
            }
            
            // Check content
            var contentReport = MemoryFragmentCleaner.AnalyzeText(fragment.Content);
            if (contentReport.HasIssues)
            {
                result.ContentIssuesFound++;
                result.FragmentsNeedingCleanup.Add(new FragmentIssue
                {
                    FragmentId = fragment.Id,
                    Category = fragment.Category,
                    Report = contentReport,
                    ContentLength = fragment.Content.Length
                });
            }
        }
        
        Console.WriteLine($"[?] Scan complete:");
        Console.WriteLine($"    Total fragments: {result.TotalFragments}");
        Console.WriteLine($"    Fragments with content issues: {result.ContentIssuesFound}");
        Console.WriteLine($"    Fragments with category issues: {result.CategoryIssuesFound}");
        
        return result;
    }
    
    /// <summary>
    /// Clean all memory fragments in a collection.
    /// </summary>
    public async Task<CleaningResult> CleanCollectionAsync(string collectionName, bool dryRun = false)
    {
        Console.WriteLine($"[*] {(dryRun ? "DRY RUN - " : "")}Cleaning collection '{collectionName}'...");
        
        var fragments = await _repository.LoadByCollectionAsync(collectionName);
        var result = new CleaningResult
        {
            CollectionName = collectionName,
            TotalFragments = fragments.Count,
            DryRun = dryRun
        };
        
        foreach (var fragment in fragments)
        {
            bool needsUpdate = false;
            
            // Clean category
            var originalCategory = fragment.Category;
            var cleanedCategory = MemoryFragmentCleaner.CleanText(originalCategory);
            if (cleanedCategory != originalCategory)
            {
                fragment.Category = cleanedCategory;
                needsUpdate = true;
                result.CategoriesCleaned++;
            }
            
            // Clean content
            var originalContent = fragment.Content;
            var cleanedContent = MemoryFragmentCleaner.CleanText(originalContent);
            if (cleanedContent != originalContent)
            {
                fragment.Content = cleanedContent;
                fragment.ContentLength = cleanedContent.Length;
                needsUpdate = true;
                result.ContentsCleaned++;
                
                // Track significant changes
                int charactersDiff = Math.Abs(originalContent.Length - cleanedContent.Length);
                if (charactersDiff > 10)
                {
                    result.SignificantChanges.Add(new CleaningChange
                    {
                        FragmentId = fragment.Id,
                        Category = originalCategory,
                        OriginalLength = originalContent.Length,
                        CleanedLength = cleanedContent.Length,
                        CharactersRemoved = charactersDiff
                    });
                }
            }
            
            // Update in database if needed and not dry run
            if (needsUpdate)
            {
                result.FragmentsUpdated++;
                
                if (!dryRun)
                {
                    // Update category
                    if (cleanedCategory != originalCategory)
                    {
                        // Note: VectorMemoryRepository doesn't have UpdateCategory method,
                        // so we need to update the entire entity
                        // For now, just update content which includes ContentLength
                    }
                    
                    // Update content
                    await _repository.UpdateContentAsync(fragment.Id, cleanedContent);
                }
                
                // Show progress every 10 fragments
                if (result.FragmentsUpdated % 10 == 0)
                {
                    Console.WriteLine($"    Progress: {result.FragmentsUpdated}/{result.TotalFragments} processed...");
                }
            }
        }
        
        Console.WriteLine($"[?] Cleaning {(dryRun ? "simulation " : "")}complete:");
        Console.WriteLine($"    Total fragments: {result.TotalFragments}");
        Console.WriteLine($"    Fragments updated: {result.FragmentsUpdated}");
        Console.WriteLine($"    Categories cleaned: {result.CategoriesCleaned}");
        Console.WriteLine($"    Contents cleaned: {result.ContentsCleaned}");
        Console.WriteLine($"    Significant changes: {result.SignificantChanges.Count}");
        
        if (result.SignificantChanges.Any())
        {
            Console.WriteLine($"\n[!] Significant changes (removed >10 characters):");
            foreach (var change in result.SignificantChanges.Take(5))
            {
                Console.WriteLine($"    - {change.Category}: {change.OriginalLength} ? {change.CleanedLength} chars ({change.CharactersRemoved} removed)");
            }
            if (result.SignificantChanges.Count > 5)
            {
                Console.WriteLine($"    ... and {result.SignificantChanges.Count - 5} more");
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Clean specific fragments by ID.
    /// </summary>
    public async Task<int> CleanFragmentsAsync(IEnumerable<Guid> fragmentIds, bool dryRun = false)
    {
        int cleanedCount = 0;
        
        foreach (var fragmentId in fragmentIds)
        {
            // Note: VectorMemoryRepository doesn't have LoadByIdAsync,
            // so we need to implement a different approach or add that method
            // For now, this is a placeholder
            Console.WriteLine($"[!] CleanFragmentsAsync not fully implemented - needs LoadByIdAsync in repository");
        }
        
        return cleanedCount;
    }
    
    /// <summary>
    /// Get statistics about all collections that might need cleaning.
    /// </summary>
    public async Task<List<CollectionCleaningStats>> GetAllCollectionStatsAsync()
    {
        var collections = await _repository.GetCollectionsAsync();
        var stats = new List<CollectionCleaningStats>();
        
        foreach (var collection in collections)
        {
            var scanResult = await ScanCollectionAsync(collection);
            stats.Add(new CollectionCleaningStats
            {
                CollectionName = collection,
                TotalFragments = scanResult.TotalFragments,
                FragmentsWithIssues = scanResult.ContentIssuesFound + scanResult.CategoryIssuesFound,
                IssuePercentage = scanResult.TotalFragments > 0 
                    ? (double)(scanResult.ContentIssuesFound + scanResult.CategoryIssuesFound) / scanResult.TotalFragments * 100 
                    : 0
            });
        }
        
        return stats.OrderByDescending(s => s.IssuePercentage).ToList();
    }
    
    #region Result Classes
    
    public class CleaningScanResult
    {
        public string CollectionName { get; set; } = string.Empty;
        public int TotalFragments { get; set; }
        public int CategoryIssuesFound { get; set; }
        public int ContentIssuesFound { get; set; }
        public List<FragmentIssue> FragmentsNeedingCleanup { get; set; } = new();
        
        public bool HasIssues => CategoryIssuesFound > 0 || ContentIssuesFound > 0;
        public double IssuePercentage => TotalFragments > 0 ? (double)(CategoryIssuesFound + ContentIssuesFound) / TotalFragments * 100 : 0;
    }
    
    public class FragmentIssue
    {
        public Guid FragmentId { get; set; }
        public string Category { get; set; } = string.Empty;
        public MemoryFragmentCleaner.CleaningReport Report { get; set; } = new();
        public int ContentLength { get; set; }
    }
    
    public class CleaningResult
    {
        public string CollectionName { get; set; } = string.Empty;
        public int TotalFragments { get; set; }
        public int FragmentsUpdated { get; set; }
        public int CategoriesCleaned { get; set; }
        public int ContentsCleaned { get; set; }
        public bool DryRun { get; set; }
        public List<CleaningChange> SignificantChanges { get; set; } = new();
    }
    
    public class CleaningChange
    {
        public Guid FragmentId { get; set; }
        public string Category { get; set; } = string.Empty;
        public int OriginalLength { get; set; }
        public int CleanedLength { get; set; }
        public int CharactersRemoved { get; set; }
    }
    
    public class CollectionCleaningStats
    {
        public string CollectionName { get; set; } = string.Empty;
        public int TotalFragments { get; set; }
        public int FragmentsWithIssues { get; set; }
        public double IssuePercentage { get; set; }
    }
    
    #endregion
}
