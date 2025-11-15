using System;

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

        // Remove special tokens from various model formats
        
        // Llama 3.2 tokens
        response = response.Replace("<|begin_of_text|>", "")
                          .Replace("<|end_of_text|>", "")
                          .Replace("<|eot_id|>", "")
                          .Replace("<|start_header_id|>", "")
                          .Replace("<|end_header_id|>", "");
        
        // TinyLlama / Phi tokens
        response = response.Replace("<|system|>", "")
                          .Replace("<|user|>", "")
                          .Replace("<|assistant|>", "")
                          .Replace("<|end|>", "")
                          .Replace("<|endoftext|>", "");

        // ChatML tokens
        response = response.Replace("<|im_start|>", "")
                          .Replace("<|im_end|>", "");

        // Mistral instruction tokens
        response = response.Replace("[INST]", "")
                          .Replace("[/INST]", "")
                          .Replace("<<SYS>>", "")
                          .Replace("<</SYS>>", "");

        // Llama 2 special tokens
        response = response.Replace("<s>", "")
                          .Replace("</s>", "");

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

        // Trim whitespace
        response = response.Trim();

        return response;
    }
}
