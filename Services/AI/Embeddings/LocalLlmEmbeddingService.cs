using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using System.Diagnostics;
using System.Text;
using Factories;
using Factories.Extensions;

namespace Services.AI.Embeddings;

/// <summary>
/// A simple embedding service that uses the local LLM to generate embeddings.
/// For better results, consider using a dedicated embedding model like all-MiniLM-L6-v2.
/// </summary>
public class LocalLlmEmbeddingService : ITextEmbeddingGenerationService
{
    private readonly string _llmPath;
    private readonly string _modelPath;
    private readonly int _embeddingDimension;

    public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

    public LocalLlmEmbeddingService(string llmPath, string modelPath, int embeddingDimension = 384)
    {
        _llmPath = llmPath ?? throw new ArgumentNullException(nameof(llmPath));
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _embeddingDimension = embeddingDimension;
    }

    public async Task<IList<ReadOnlyMemory<float>>> GenerateEmbeddingsAsync(
        IList<string> data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReadOnlyMemory<float>>();
        foreach (var text in data)
        {
            var embedding = await GenerateEmbeddingAsync(text, kernel, cancellationToken);
            results.Add(embedding);
        }

        return results;
    }

    public Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
        string data,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        // Create a simple hash-based embedding
        // This is a placeholder - for production, use a proper embedding model
        var embedding = CreateSimpleEmbedding(data);
        return Task.FromResult(embedding);
    }

    /// <summary>
    /// Creates a simple embedding based on character frequencies and n-grams.
    /// For better results, use a proper embedding model like sentence-transformers.
    /// </summary>
    private ReadOnlyMemory<float> CreateSimpleEmbedding(string text)
    {
        var embedding = new float[_embeddingDimension];
        var normalized = text.ToLowerInvariant();

        // Feature 1: Character frequency distribution (26 dimensions for a-z)
        for (int i = 0; i < 26 && i < _embeddingDimension; i++)
        {
            char targetChar = (char)('a' + i);
            embedding[i] = normalized.Count(c => c == targetChar) / (float)Math.Max(1, normalized.Length);
        }

        // Feature 2: Digit frequency (10 dimensions for 0-9)
        for (int i = 0; i < 10 && i + 26 < _embeddingDimension; i++)
        {
            char targetDigit = (char)('0' + i);
            embedding[26 + i] = normalized.Count(c => c == targetDigit) / (float)Math.Max(1, normalized.Length);
        }

        // Feature 3: Common bigrams (50 dimensions)
        var bigrams = new List<string>();
        for (int i = 0; i < normalized.Length - 1; i++)
        {
            if (char.IsLetter(normalized[i]) && char.IsLetter(normalized[i + 1]))
            {
                bigrams.Add(normalized.Substring(i, 2));
            }
        }
        
        var commonBigrams = new[] { "th", "he", "in", "er", "an", "re", "on", "at", "en", "nd",
                                     "ti", "es", "or", "te", "of", "ed", "is", "it", "al", "ar",
                                     "st", "to", "nt", "ng", "se", "ha", "as", "ou", "io", "le",
                                     "ve", "co", "me", "de", "hi", "ri", "ro", "ic", "ne", "ea",
                                     "ra", "ce", "li", "ch", "ll", "be", "ma", "si", "om", "ur" };
        
        for (int i = 0; i < commonBigrams.Length && i + 36 < _embeddingDimension; i++)
        {
            embedding[36 + i] = bigrams.Count(b => b == commonBigrams[i]) / (float)Math.Max(1, bigrams.Count);
        }

        // Feature 4: Common trigrams (50 dimensions)
        var trigrams = new List<string>();
        for (int i = 0; i < normalized.Length - 2; i++)
        {
            if (char.IsLetter(normalized[i]) && char.IsLetter(normalized[i + 1]) && char.IsLetter(normalized[i + 2]))
            {
                trigrams.Add(normalized.Substring(i, 3));
            }
        }
        
        var commonTrigrams = new[] { "the", "and", "ing", "ion", "tio", "ent", "ati", "for", "her", "ter",
                                      "hat", "tha", "ere", "ate", "his", "con", "res", "ver", "all", "ons",
                                      "nce", "men", "ith", "ted", "ers", "pro", "thi", "wit", "are", "ess",
                                      "not", "ive", "was", "ect", "rea", "com", "eve", "per", "int", "est",
                                      "sta", "cti", "ica", "ist", "ear", "ain", "one", "our", "iti", "rat" };
        
        for (int i = 0; i < commonTrigrams.Length && i + 86 < _embeddingDimension; i++)
        {
            embedding[86 + i] = trigrams.Count(t => t == commonTrigrams[i]) / (float)Math.Max(1, trigrams.Count);
        }

        // Feature 5: Common words + Domain-specific vocabulary (148 dimensions - extended from 100)
        var words = normalized.Split(new[] { ' ', '\t', '\r', '\n', '.', ',', '!', '?', ';', ':' }, 
                                     StringSplitOptions.RemoveEmptyEntries);
        
        // Extended word list: 100 common English words + 48 game/domain-specific words
        var commonWords = new[] { 
            // Original 100 common English words
            "the", "be", "to", "of", "and", "a", "in", "that", "have", "i",
            "it", "for", "not", "on", "with", "he", "as", "you", "do", "at",
            "this", "but", "his", "by", "from", "they", "we", "say", "her", "she",
            "or", "an", "will", "my", "one", "all", "would", "there", "their", "what",
            "so", "up", "out", "if", "about", "who", "get", "which", "go", "me",
            "when", "make", "can", "like", "time", "no", "just", "him", "know", "take",
            "people", "into", "year", "your", "good", "some", "could", "them", "see", "other",
            "than", "then", "now", "look", "only", "come", "its", "over", "think", "also",
            "back", "after", "use", "two", "how", "our", "work", "first", "well", "way",
            "even", "new", "want", "because", "any", "these", "give", "day", "most", "us",
            
            // Domain-specific vocabulary for board games / Treasure Hunt (48 new words)
            // Game mechanics
            "win", "winner", "victory", "lose", "loser", "defeat",
            "game", "play", "player", "players", "turn", "round",
            "roll", "die", "dice", "card", "cards", "draw", "drawn",
            "move", "movement", "space", "spaces", "room", "rooms",
            
            // Treasure Hunt specific
            "treasure", "treasures", "gold", "bonus", "value",
            "monster", "monsters", "fight", "fighting", "attack", "defend",
            "power", "strength", "damage",
            
            // General game terms
            "rules", "rule", "score", "points", "help", "helper",
            "alone", "together", "hand", "deck"
        };
        
        // Adjust dimension calculation for extended word list (148 words instead of 100)
        for (int i = 0; i < commonWords.Length && i + 136 < _embeddingDimension; i++)
        {
            embedding[136 + i] = words.Count(w => w == commonWords[i]) / (float)Math.Max(1, words.Length);
        }

        // Feature 6: Basic text statistics (remaining dimensions - adjusted for extended words)
        int statsStart = 284; // 136 + 148 = 284 (was 236)
        if (statsStart < _embeddingDimension)
        {
            embedding[statsStart] = normalized.Length / 1000f; // Text length
            if (statsStart + 1 < _embeddingDimension)
                embedding[statsStart + 1] = words.Length / 100f; // Word count
            if (statsStart + 2 < _embeddingDimension)
                embedding[statsStart + 2] = normalized.Count(char.IsLetter) / (float)Math.Max(1, normalized.Length);
            if (statsStart + 3 < _embeddingDimension)
                embedding[statsStart + 3] = normalized.Count(char.IsDigit) / (float)Math.Max(1, normalized.Length);
            if (statsStart + 4 < _embeddingDimension)
                embedding[statsStart + 4] = normalized.Count(char.IsWhiteSpace) / (float)Math.Max(1, normalized.Length);
            if (statsStart + 5 < _embeddingDimension)
                embedding[statsStart + 5] = normalized.Count(char.IsPunctuation) / (float)Math.Max(1, normalized.Length);
            if (statsStart + 6 < _embeddingDimension)
                embedding[statsStart + 6] = words.Length > 0 ? (float)words.Average(w => w.Length) / 10f : 0;
            if (statsStart + 7 < _embeddingDimension)
                embedding[statsStart + 7] = normalized.Count(c => char.IsUpper(text.IndexOf(c) >= 0 ? text[text.IndexOf(c)] : 'a')) / (float)Math.Max(1, text.Length);
        }

        // Normalize the vector to unit length
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
            {
                embedding[i] /= (float)magnitude;
            }
        }

        return new ReadOnlyMemory<float>(embedding);
    }
}