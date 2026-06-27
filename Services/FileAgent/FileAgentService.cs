namespace Services.FileAgent;

/// <summary>
/// Implementation of <see cref="IFileAgentService"/> that manages text files in a
/// configurable base directory. Handles the following chat slash commands:
/// <list type="bullet">
///   <item><c>/skapa &lt;filename&gt;</c> — creates an empty file.</item>
///   <item><c>/fyll &lt;filename&gt; &lt;content&gt;</c> — writes content to a file.</item>
///   <item><c>/läs &lt;filename&gt;</c> (or <c>/las</c>) — reads the file and returns its
///     content as <see cref="FileAgentResult.InjectedContext"/> to be forwarded to the AI.</item>
/// </list>
/// All operations are restricted to <see cref="BaseDirectory"/> to prevent path traversal.
/// </summary>
public class FileAgentService : IFileAgentService
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    // Markers that delimit the file content block in LLM responses.
    // The LLM is instructed to wrap the file content between these two lines.
    private const string FileStartMarker = "<<<FILE>>>";
    private const string FileEndMarker = "<<<ENDFILE>>>";

    /// <inheritdoc/>
    public string BaseDirectory { get; }

    /// <param name="baseDirectory">
    /// Directory in which agent files are created and read.
    /// The directory is created automatically if it does not exist.
    /// </param>
    public FileAgentService(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentNullException(nameof(baseDirectory));

        BaseDirectory = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(BaseDirectory);
    }

    /// <inheritdoc/>
    public bool IsCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var t = input.TrimStart();
        return t.StartsWith("/skapa ", Cmp)
            || t.StartsWith("/fyll ", Cmp)
            || t.StartsWith("/läs ", Cmp)
            || t.StartsWith("/las ", Cmp);
    }

    /// <inheritdoc/>
    public async Task<FileAgentResult> ExecuteAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return FileAgentResult.Failure("Tomt kommando.");

        var trimmed = input.Trim();

        if (trimmed.StartsWith("/skapa ", Cmp))
            return await CreateFileAsync(trimmed["/skapa ".Length..].Trim());

        if (trimmed.StartsWith("/fyll ", Cmp))
            return await FillFileAsync(trimmed["/fyll ".Length..].Trim());

        if (trimmed.StartsWith("/läs ", Cmp))
            return await ReadFileAsync(trimmed["/läs ".Length..].Trim());

        if (trimmed.StartsWith("/las ", Cmp))
            return await ReadFileAsync(trimmed["/las ".Length..].Trim());

        return FileAgentResult.NotACommand();
    }

    // ── /skapa ──────────────────────────────────────────────────────────────

    private async Task<FileAgentResult> CreateFileAsync(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return FileAgentResult.Failure("Ange ett filnamn. Exempel: /skapa text.txt");

        var path = GetSafePath(filename);
        if (path is null)
            return FileAgentResult.Failure($"Ogiltigt filnamn: \"{filename}\".");

        await File.WriteAllTextAsync(path, string.Empty);
        return FileAgentResult.Success(FileAgentResultType.FileCreated, $"✓ Fil skapad: {Path.GetFileName(path)}");
    }

    // ── /fyll ───────────────────────────────────────────────────────────────

    private Task<FileAgentResult> FillFileAsync(string args)
    {
        // Format: <filename> <prompt>
        var spaceIdx = args.IndexOf(' ');
        if (spaceIdx < 0)
            return Task.FromResult(FileAgentResult.Failure(
                "Ange filnamn och beskrivning. Exempel: /fyll config.json Skapa konfiguration för Blazor-app"));

        var filename = args[..spaceIdx].Trim();
        var userPrompt = args[(spaceIdx + 1)..].Trim();

        if (GetSafePath(filename) is null)
            return Task.FromResult(FileAgentResult.Failure($"Ogiltigt filnamn: \"{filename}\"."));

        var llmPrompt =
            $"Du ska generera innehållet för filen \"{filename}\".\n" +
            $"Uppgift: {userPrompt}\n\n" +
            $"VIKTIGT: Skriv EXAKT det som ska sparas i filen mellan dessa två markeringar " +
            $"och ingenting annat utanför dem:\n" +
            $"{FileStartMarker}\n" +
            $"{FileEndMarker}\n\n" +
            $"Placera filinnehållet mellan {FileStartMarker} och {FileEndMarker}. " +
            $"Inga förklaringar utanför markörerna.";

        return Task.FromResult(FileAgentResult.FillRequest(filename, llmPrompt));
    }

    // ── /läs ────────────────────────────────────────────────────────────────

    private async Task<FileAgentResult> ReadFileAsync(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return FileAgentResult.Failure("Ange ett filnamn. Exempel: /läs text.txt");

        var path = GetSafePath(filename);
        if (path is null)
            return FileAgentResult.Failure($"Ogiltigt filnamn: \"{filename}\".");

        if (!File.Exists(path))
            return FileAgentResult.Failure(
                $"Filen hittades inte: {Path.GetFileName(path)}\n" +
                $"Skapa den med: /skapa {filename}");

        var content = await File.ReadAllTextAsync(path);
        if (string.IsNullOrWhiteSpace(content))
            return FileAgentResult.Failure($"Filen är tom: {Path.GetFileName(path)}");

        return FileAgentResult.ReadSuccess(
            $"✓ Fil läst: {Path.GetFileName(path)}",
            content);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool TryExtractFileContent(string llmResponse, out string content)
    {
        content = string.Empty;
        if (string.IsNullOrWhiteSpace(llmResponse)) return false;

        var start = llmResponse.IndexOf(FileStartMarker, Cmp);
        var end = llmResponse.IndexOf(FileEndMarker, Cmp);

        if (start < 0 || end < 0 || end <= start) return false;

        var contentStart = start + FileStartMarker.Length;
        content = llmResponse[contentStart..end].Trim();
        return !string.IsNullOrWhiteSpace(content);
    }

    /// <inheritdoc/>
    public async Task WriteExtractedContentAsync(string filename, string content)
    {
        var path = GetSafePath(filename)
            ?? throw new InvalidOperationException($"Ogiltigt filnamn: \"{filename}\".");
        await File.WriteAllTextAsync(path, content);
    }

    /// <inheritdoc/>
    public string StripFileMarkers(string llmResponse)
    {
        return llmResponse
            .Replace(FileStartMarker, string.Empty, Cmp)
            .Replace(FileEndMarker, string.Empty, Cmp)
            .Trim();
    }

    /// <summary>
    /// Returns the full path for <paramref name="filename"/> inside <see cref="BaseDirectory"/>,
    /// or <c>null</c> if the filename is invalid or attempts path traversal.
    /// </summary>
    private string? GetSafePath(string filename)
    {
        // Strip any directory component — only bare filenames are allowed.
        var safeName = Path.GetFileName(filename.Trim());
        if (string.IsNullOrWhiteSpace(safeName)) return null;

        var fullPath = Path.GetFullPath(Path.Combine(BaseDirectory, safeName));

        // Ensure the resolved path stays within BaseDirectory.
        if (!fullPath.StartsWith(BaseDirectory, StringComparison.OrdinalIgnoreCase))
            return null;

        return fullPath;
    }
}
