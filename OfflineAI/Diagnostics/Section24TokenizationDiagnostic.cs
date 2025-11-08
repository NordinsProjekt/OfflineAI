using BERTTokenizers;
using Services.Utilities;
using System.Diagnostics;

namespace OfflineAI.Diagnostics;

/// <summary>
/// Diagnostic tool to test BERT tokenization with problematic Section 24 text.
/// Run this to verify the fix works with the actual text that was causing errors.
/// </summary>
public static class Section24TokenizationDiagnostic
{
    private const string Section24Text = @"Move Monsters 
Move each Monster 1 space closer to the Castle or 1 space clockwise if inside  the Castle. If a Monster hits a Wall or Tower, the Monster takes 1 point of  damage and the Wall or Tower is destroyed. If the Monster has health points  remaining after destroying a Wall, the Monster stays in the Swordsman ring. If the Monster has health points remaining after destroying a Tower,  the Monster moves into the space  vacated by the Tower.  If more than 1 Monster hits a Wall  or Tower, players choose which  Monster takes the damage. If hitting  a Wall, all of the Monsters stay in  the Swordsman ring. If hitting a  Tower, all of the Monsters move  into the Tower space.  The exceptions are the 4- and  5-point Monsters. If they are at their  lowest point, they take no damage  from hitting a Castle structure.  Monsters affected by Flask of Glue  or the Sleep Potion do not move. 6. Place Monsters Draw new Monsters one at a time from the Monster  bag and place them in the Forest. The number of  Monsters drawn depends on the number of players. If you draw a Curse (or 2 or 3 or more), resolve  it and draw another Monster to place. Use the die to  place each Monster in the Forest. Place Monsters with  the largest number pointed toward the Castle. This is the Monster's starting  health points. The black edge on some Monsters has meaning only for the  More Munchkin Mini-Expansion (p. 9).";

    public static async Task RunAsync()
    {
        Console.WriteLine("?????????????????????????????????????????????????????????????");
        Console.WriteLine("?  Section 24 Tokenization Diagnostic                      ?");
        Console.WriteLine("?  Configuration: maxSequenceLength = 512 (MODEL MAXIMUM)   ?");
        Console.WriteLine("?  Safe Character Limit: 1,536 chars (512 × 3)             ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????");
        Console.WriteLine();
        
        Console.WriteLine("Testing the exact text that caused the original error...");
        Console.WriteLine();
        
        // Step 1: Show original text stats
        Console.WriteLine("?? ORIGINAL TEXT");
        Console.WriteLine(new string('?', 60));
        Console.WriteLine($"Length: {Section24Text.Length} characters");
        Console.WriteLine($"Preview: {Section24Text.Substring(0, Math.Min(100, Section24Text.Length))}...");
        Console.WriteLine();
        
        // Step 2: Normalize the text
        Console.WriteLine("?? NORMALIZATION");
        Console.WriteLine(new string('?', 60));
        var stopwatch = Stopwatch.StartNew();
        var normalized = TextNormalizer.NormalizeWithLimits(Section24Text, maxLength: 5000);
        stopwatch.Stop();
        
        Console.WriteLine($"? Normalized in {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"Length after normalization: {normalized.Length} characters");
        Console.WriteLine($"Is empty/whitespace? {string.IsNullOrWhiteSpace(normalized)}");
        Console.WriteLine();
        
        // Step 3: Tokenize
        Console.WriteLine("?? TOKENIZATION");
        Console.WriteLine(new string('?', 60));
        
        try
        {
            var tokenizer = new BertUncasedLargeTokenizer();
            stopwatch.Restart();
            var tokenization = BertTokenizationHelper.TokenizeWithFallback(
                tokenizer,
                normalized,
                maxSequenceLength: 256);
            stopwatch.Stop();
            
            Console.WriteLine($"? Tokenized in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Input IDs length: {tokenization.InputIds.Length}");
            Console.WriteLine($"Actual token count: {tokenization.ActualTokenCount}");
            Console.WriteLine($"Attention mask length: {tokenization.AttentionMask.Length}");
            Console.WriteLine($"Token type IDs length: {tokenization.TokenTypeIds.Length}");
            Console.WriteLine();
            
            // Step 4: Create tensors
            Console.WriteLine("?? TENSOR CREATION");
            Console.WriteLine(new string('?', 60));
            
            stopwatch.Restart();
            var tensors = BertTokenizationHelper.CreateInputTensors(tokenization);
            stopwatch.Stop();
            
            Console.WriteLine($"? Tensors created in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"Tensor count: {tensors.Count}");
            Console.WriteLine($"Tensor names: {string.Join(", ", tensors.Keys)}");
            
            var inputIdsTensor = tensors["input_ids"];
            Console.WriteLine($"Input IDs tensor shape: [{string.Join(", ", inputIdsTensor.Dimensions.ToArray())}]");
            Console.WriteLine();
            
            // Step 5: Show first few tokens
            Console.WriteLine("?? TOKEN DETAILS");
            Console.WriteLine(new string('?', 60));
            Console.WriteLine("First 10 tokens:");
            for (int i = 0; i < Math.Min(10, tokenization.InputIds.Length); i++)
            {
                Console.WriteLine($"  Token {i}: ID={tokenization.InputIds[i]}, " +
                                $"Attention={tokenization.AttentionMask[i]}, " +
                                $"TypeID={tokenization.TokenTypeIds[i]}");
            }
            Console.WriteLine();
            
            // Success!
            Console.WriteLine("?????????????????????????????????????????????????????????????");
            Console.WriteLine("?  ? SUCCESS! All steps completed without errors           ?");
            Console.WriteLine("?????????????????????????????????????????????????????????????");
            Console.WriteLine();
            Console.WriteLine("The Section 24 text is now handled correctly.");
            Console.WriteLine("The fix prevents the 'count parameter out of range' error.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("?????????????????????????????????????????????????????????????");
            Console.WriteLine("?  ? ERROR OCCURRED                                         ?");
            Console.WriteLine("?????????????????????????????????????????????????????????????");
            Console.WriteLine();
            Console.WriteLine($"Exception Type: {ex.GetType().Name}");
            Console.WriteLine($"Message: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Stack Trace:");
            Console.WriteLine(ex.StackTrace);
            
            throw;
        }
    }
    
    /// <summary>
    /// Tests multiple problematic text patterns that might cause tokenization issues.
    /// </summary>
    public static async Task RunMultiplePatternTestsAsync()
    {
        Console.WriteLine("?????????????????????????????????????????????????????????????");
        Console.WriteLine("?  Multiple Pattern Tests                                  ?");
        Console.WriteLine("?????????????????????????????????????????????????????????????");
        Console.WriteLine();
        
        var testPatterns = new Dictionary<string, string>
        {
            ["Multiple Spaces"] = "inside  the  Castle   with    extra     spaces",
            ["Numbers and Dashes"] = "4- and 5-point Monsters",
            ["Parentheses"] = "More Munchkin Mini-Expansion (p. 9)",
            ["Mixed Whitespace"] = "Line 1\nLine 2\t\tTabbed\r\n\r\nDouble newline",
            ["Only Whitespace"] = "   \t\n   \r\n   ",
            ["Special Characters"] = "@#$%^&*()!+=[]{}|;:',.<>?/",
            ["Unicode"] = "???? ????? ??????",
            ["Empty String"] = "",
            ["Very Long Text"] = new string('x', 10000)
        };
        
        var tokenizer = new BertUncasedLargeTokenizer();
        int passed = 0;
        int failed = 0;
        
        foreach (var (name, text) in testPatterns)
        {
            Console.Write($"Testing '{name}'... ");
            
            try
            {
                var normalized = TextNormalizer.NormalizeWithLimits(text, maxLength: 5000);
                var tokenization = BertTokenizationHelper.TokenizeWithFallback(
                    tokenizer,
                    normalized,
                    maxSequenceLength: 256);
                var tensors = BertTokenizationHelper.CreateInputTensors(tokenization);
                
                Console.WriteLine($"? PASS ({tokenization.ActualTokenCount} tokens)");
                passed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? FAIL: {ex.Message}");
                failed++;
            }
        }
        
        Console.WriteLine();
        Console.WriteLine(new string('?', 60));
        Console.WriteLine($"Results: {passed} passed, {failed} failed out of {testPatterns.Count} tests");
        
        if (failed == 0)
        {
            Console.WriteLine("? All patterns handled successfully!");
        }
        else
        {
            Console.WriteLine($"? {failed} pattern(s) failed - review needed");
        }
    }
    
    /// <summary>
    /// Tests a realistic 2000+ character rulebook section to show the limitation.
    /// Demonstrates why chunking is needed for long sections.
    /// </summary>
    public static async Task Test2000CharacterSectionAsync()
    {
        Console.WriteLine("?????????????????????????????????????????????????????????????");
        Console.WriteLine("?  2000+ Character Section Test                            ?");
        Console.WriteLine("?  IMPORTANT: Model limit is 512 tokens (1,536 chars)      ?");
        Console.WriteLine("?  This test shows truncation - USE CHUNKING for long text?");
        Console.WriteLine("?????????????????????????????????????????????????????????????");
        Console.WriteLine();
        
        // Create a realistic 2000+ character rulebook section
        var longSection = @"
Complete Rulebook Section - Combat Resolution

When a player initiates combat with a monster, follow these detailed steps to resolve the encounter completely and fairly for all participants involved in the game session.

Step 1: Determine Combat Values
Each player begins by calculating their total combat strength. This includes the base level of the character, any bonuses from equipment currently held and deployed, temporary buffs from potions or spells cast in previous turns, and any special abilities that may be activated during this specific combat scenario. The monster's combat value is printed clearly on its card, though this may be modified by certain dungeon effects or curse cards that were drawn earlier in the game sequence.

Step 2: Compare Strengths  
Once all values are calculated and verified by all players, compare the player's total combat strength against the monster's total combat value. If the player's value is strictly greater than the monster's value, the player wins the combat and proceeds to Step 3 for rewards. If the values are exactly equal, the combat is considered a tie, and the player must flee as described in Step 4. If the monster's value is greater, the player loses and must also flee, potentially suffering additional bad stuff penalties.

Step 3: Victory Rewards
Upon defeating a monster in combat, the victorious player gains rewards immediately. First, the player advances their character level by the number of levels indicated on the monster card - typically one level for common monsters, but potentially more for boss-level encounters. Second, the player draws treasure cards from the appropriate deck, drawing a number of cards equal to the treasure value printed on the monster card. These treasures are added to the player's hand and may be played immediately if applicable to the current situation.

Step 4: Fleeing from Combat
If a player cannot win the combat, they must attempt to flee. Roll a standard six-sided die. If the result is 5 or higher, the player successfully escapes without penalty and the monster card is discarded. If the roll is 4 or lower, the fleeing attempt fails and the player must suffer the bad stuff printed on the monster card. Bad stuff varies widely - some monsters merely cause the player to lose levels, others force the player to discard equipment, and the most dangerous monsters may even result in character death requiring full resurrection procedures.

Special Cases and Exceptions
Certain character classes have special abilities that modify these combat rules significantly. For example, Warriors may ignore the first monster's special ability, Wizards can cast spells to modify combat values mid-combat, and Thieves have improved fleeing chances. Additionally, other players may interfere in combat by playing cards that help or hinder the active player, potentially changing the outcome dramatically at the last moment.";

        Console.WriteLine($"?? Test Section Length: {longSection.Length} characters");
        Console.WriteLine($"   MODEL LIMIT: 1,536 chars (512 tokens × 3)");
        Console.WriteLine($"   ??  WILL BE TRUNCATED - last ~464 chars lost");
        Console.WriteLine($"   ?? Solution: Use chunking for sections >1500 chars");
        Console.WriteLine();
        
        var tokenizer = new BERTTokenizers.BertUncasedLargeTokenizer();
        
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Normalize
            var normalized = TextNormalizer.NormalizeWithLimits(longSection, maxLength: 5000);
            Console.WriteLine($"? Normalized: {normalized.Length} chars");
            
            // Tokenize with maxSequenceLength = 512 (MODEL MAXIMUM - cannot be changed)
            var tokenization = BertTokenizationHelper.TokenizeWithFallback(
                tokenizer,
                normalized,
                maxSequenceLength: 512);  // MODEL MAXIMUM
            
            stopwatch.Stop();
            
            Console.WriteLine($"? Tokenized: {tokenization.ActualTokenCount} tokens (max: 512)");
            
            // Check if truncation occurred
            bool wasTruncated = normalized.Length > 512 * 3;
            Console.WriteLine($"??  Truncation occurred: {wasTruncated}");
            
            if (wasTruncated)
            {
                int charsLost = normalized.Length - (512 * 3);
                Console.WriteLine($"   Characters lost: {charsLost} ({(charsLost * 100.0 / normalized.Length):F1}% of content)");
            }
            
            Console.WriteLine($"? Time: {stopwatch.ElapsedMilliseconds}ms");
            
            Console.WriteLine();
            Console.WriteLine("?????????????????????????????????????????????????????????????");
            Console.WriteLine("?  ??  TRUNCATION WARNING                                   ?");
            Console.WriteLine("?  2000+ char sections don't fit in 512 token limit        ?");
            Console.WriteLine("?                                                           ?");
            Console.WriteLine("?  SOLUTION: Use chunking strategy                         ?");
            Console.WriteLine("?  LoadFromFileWithChunkingAsync(maxChunkSize: 1400)       ?");
            Console.WriteLine("?                                                           ?");
            Console.WriteLine("?  This splits long sections into multiple embeddings      ?");
            Console.WriteLine("?  See: Docs/BERT-Model-512-Token-Limit.md                 ?");
            Console.WriteLine("?????????????????????????????????????????????????????????????");
        }
        catch (Exception ex)
        {
            Console.WriteLine("?????????????????????????????????????????????????????????????");
            Console.WriteLine("?  ? ERROR                                                  ?");
            Console.WriteLine("?????????????????????????????????????????????????????????????");
            Console.WriteLine($"Exception: {ex.Message}");
        }
    }
}
