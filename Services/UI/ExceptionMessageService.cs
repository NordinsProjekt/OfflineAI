namespace Services.UI;

/// <summary>
/// Centralized service for generating consistent exception messages.
/// Keeps error messaging logic separate from business logic.
/// </summary>
public static class ExceptionMessageService
{
    /// <summary>
    /// Generates a detailed error message for missing BERT model file.
    /// </summary>
    /// <param name="modelPath">The expected path where the model should be located</param>
    /// <returns>Formatted error message with download instructions</returns>
    public static string BertModelNotFound(string modelPath)
    {
        return $"BERT model not found at: {modelPath}\n\n" +
               $"Download from: https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx/model.onnx\n" +
               $"Place in: d:\\tinyllama\\models\\all-MiniLM-L6-v2\\model.onnx\n\n" +
               $"Or run: .\\Scripts\\Download-BERT-Model.ps1";
    }

    /// <summary>
    /// Generates a detailed error message for missing vocabulary file.
    /// </summary>
    /// <param name="vocabPath">The expected path where the vocabulary file should be located</param>
    /// <returns>Formatted error message with download instructions</returns>
    public static string BertVocabNotFound(string vocabPath)
    {
        return $"BERT vocabulary file not found at: {vocabPath}\n\n" +
               $"Download from: https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt\n" +
               $"Place in same directory as model.onnx\n\n" +
               $"PowerShell command:\n" +
               $"Invoke-WebRequest -Uri 'https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/vocab.txt' -OutFile '{vocabPath}'";
    }

    /// <summary>
    /// Generates an error message for file not found scenarios.
    /// </summary>
    /// <param name="filePath">The file path that was not found</param>
    /// <param name="fileType">Description of the file type (e.g., "text file", "configuration file")</param>
    /// <returns>Formatted error message</returns>
    public static string FileNotFound(string filePath, string fileType = "file")
    {
        return $"{fileType} not found: {filePath}";
    }

    /// <summary>
    /// Generates an error message for invalid model paths.
    /// </summary>
    /// <param name="parameterName">The name of the parameter that contained the invalid path</param>
    /// <returns>Formatted error message</returns>
    public static string InvalidModelPath(string parameterName)
    {
        return $"Invalid model path provided in parameter: {parameterName}";
    }

    /// <summary>
    /// Generates an error message for tokenization failures.
    /// </summary>
    /// <param name="textLength">Length of the text that failed to tokenize</param>
    /// <param name="reason">The underlying reason for the failure</param>
    /// <returns>Formatted error message</returns>
    public static string TokenizationFailed(int textLength, string reason)
    {
        return $"Tokenization failed for text (length: {textLength}): {reason}";
    }

    /// <summary>
    /// Generates an error message for embedding generation failures.
    /// </summary>
    /// <param name="exceptionType">The type of exception that occurred</param>
    /// <param name="message">The exception message</param>
    /// <returns>Formatted error message</returns>
    public static string EmbeddingGenerationFailed(string exceptionType, string message)
    {
        return $"Embedding generation failed - {exceptionType}: {message}";
    }

    /// <summary>
    /// Generates an error message for GPU acceleration failures.
    /// </summary>
    /// <param name="provider">The GPU provider that failed (e.g., "DirectML", "CUDA")</param>
    /// <param name="reason">The reason for the failure</param>
    /// <returns>Formatted error message</returns>
    public static string GpuAccelerationFailed(string provider, string reason)
    {
        return $"{provider} GPU acceleration failed: {reason}";
    }

    /// <summary>
    /// Generates an error message for database connection failures.
    /// </summary>
    /// <param name="serverName">The database server name or connection string</param>
    /// <param name="reason">The reason for the connection failure</param>
    /// <returns>Formatted error message</returns>
    public static string DatabaseConnectionFailed(string serverName, string reason)
    {
        return $"Database connection failed to {serverName}: {reason}";
    }

    /// <summary>
    /// Generates an error message for invalid configuration values.
    /// </summary>
    /// <param name="configKey">The configuration key that has an invalid value</param>
    /// <param name="expectedRange">Description of the expected value range</param>
    /// <param name="actualValue">The actual value that was provided</param>
    /// <returns>Formatted error message</returns>
    public static string InvalidConfiguration(string configKey, string expectedRange, object actualValue)
    {
        return $"Invalid configuration for '{configKey}': expected {expectedRange}, but got {actualValue}";
    }

    /// <summary>
    /// Generates an error message for unsupported model formats.
    /// </summary>
    /// <param name="modelPath">Path to the model file</param>
    /// <param name="expectedFormats">List of supported formats</param>
    /// <returns>Formatted error message</returns>
    public static string UnsupportedModelFormat(string modelPath, params string[] expectedFormats)
    {
        var formats = string.Join(", ", expectedFormats);
        return $"Unsupported model format: {modelPath}\n" +
               $"Expected formats: {formats}";
    }
}
