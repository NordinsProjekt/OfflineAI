namespace Application.AI.Extensions;

/// <summary>
/// Extension methods for string manipulation and cleaning.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Remove model-specific artifacts from LLM responses.
    /// Handles Llama 3.2, TinyLlama, Mistral, Llama 2, Phi, ChatML, and other common model formats.
    /// </summary>
    /// <param name="response">The raw LLM response string</param>
    /// <returns>Cleaned response with model-specific tokens removed</returns>
    public static string CleanModelArtifacts(this string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return response;

        // Apply cleaning for each model format
        response = CleanLlama32Tokens(response);
        response = CleanTinyLlamaPhiTokens(response);
        response = CleanChatMLTokens(response);
        response = CleanMistralTokens(response);
        response = CleanLlama2Tokens(response);
        response = CleanPlainAssistantPrefix(response);
        response = RemoveIncompleteTokens(response);

        return response.Trim();
    }

    /// <summary>
    /// Remove Llama 3.2 specific tokens.
    /// </summary>
    private static string CleanLlama32Tokens(string response)
    {
        return response
            .Replace("<|begin_of_text|>", "")
            .Replace("<|end_of_text|>", "")
            .Replace("<|eot_id|>", "")
            .Replace("<|start_header_id|>", "")
            .Replace("<|end_header_id|>", "");
    }

    /// <summary>
    /// Remove TinyLlama and Phi model specific tokens.
    /// </summary>
    private static string CleanTinyLlamaPhiTokens(string response)
    {
        return response
            .Replace("<|system|>", "")
            .Replace("<|user|>", "")
            .Replace("<|assistant|>", "")
            .Replace("<|end|>", "")
            .Replace("<|endoftext|>", "");
    }

    /// <summary>
    /// Remove ChatML format tokens.
    /// </summary>
    private static string CleanChatMLTokens(string response)
    {
        return response
            .Replace("<|im_start|>", "")
            .Replace("<|im_end|>", "");
    }

    /// <summary>
    /// Remove Mistral instruction format tokens.
    /// </summary>
    private static string CleanMistralTokens(string response)
    {
        return response
            .Replace("[INST]", "")
            .Replace("[/INST]", "")
            .Replace("<<SYS>>", "")
            .Replace("<</SYS>>", "");
    }

    /// <summary>
    /// Remove Llama 2 special tokens.
    /// </summary>
    private static string CleanLlama2Tokens(string response)
    {
        return response
            .Replace("<s>", "")
            .Replace("</s>", "");
    }

    /// <summary>
    /// Remove plain "assistant:" or "assistant" prefix that some models add.
    /// </summary>
    private static string CleanPlainAssistantPrefix(string response)
    {
        // Trim first
        response = response.Trim();

        // Check for "assistant:" or "Assistant:" at the start (case-insensitive)
        if (response.StartsWith("assistant:", StringComparison.OrdinalIgnoreCase))
        {
            response = response.Substring("assistant:".Length).TrimStart();
        }
        else if (response.StartsWith("assistant", StringComparison.OrdinalIgnoreCase) &&
                 response.Length > "assistant".Length &&
                 char.IsWhiteSpace(response["assistant".Length]))
        {
            response = response.Substring("assistant".Length).TrimStart();
        }

        return response;
    }

    /// <summary>
    /// Remove incomplete sentence markers and trailing artifacts.
    /// </summary>
    private static string RemoveIncompleteTokens(string response)
    {
        // Remove incomplete sentence markers
        if (response.EndsWith(">") && !response.EndsWith(">>"))
        {
            var lastCompleteStop = Math.Max(
                response.LastIndexOf('.'),
                Math.Max(response.LastIndexOf('!'), response.LastIndexOf('?'))
            );

            if (lastCompleteStop > 0 && lastCompleteStop < response.Length - 10)
            {
                response = response.Substring(0, lastCompleteStop + 1);
            }
        }

        return response;
    }
}
