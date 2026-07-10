using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace Services.FileAgent;

/// <summary>
/// Implementation of <see cref="IFileAgentService"/> that manages text files in a
/// configurable base directory. Handles the following chat slash commands:
/// <list type="bullet">
///   <item><c>/skapa &lt;filename&gt;</c> — creates an empty file.</item>
///   <item><c>/fyll &lt;filename&gt; &lt;content&gt;</c> — writes content to a file.</item>
///   <item><c>/läs &lt;filename&gt; &lt;instruktion&gt;</c> (or <c>/las</c>) — reads the file and
///     combines its content with the given instruction into
///     <see cref="FileAgentResult.InjectedContext"/>, so the command carries an explicit
///     instruction for the agent instead of forwarding the raw file as the entire prompt.</item>
///   <item><c>/redigera &lt;filename&gt; &lt;instruktion&gt;</c> — reads the file with line
///     numbers, asks the LLM which line ranges to replace (or where to insert brand-new code,
///     e.g. a new method) and with what, then applies the resulting <see cref="LineEdit"/>
///     changes to the file.</item>
/// </list>
/// All operations are restricted to <see cref="BaseDirectory"/> to prevent path traversal.
/// </summary>
public class FileAgentService : IFileAgentService
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    // Markers that delimit the file content block in LLM responses.
    // The LLM is instructed to wrap the file content between these two lines.
    private const string FileStartMarker = "<FILE>";
    private const string FileEndMarker = "<ENDFILE>";

    // Tags that delimit repeatable line-edit blocks in LLM responses for /redigera.
    // Replace:       <REDIGERA RAD=N> ... </REDIGERA>  or  <REDIGERA RAD=N-M> ... </REDIGERA>
    // Insert after:  <REDIGERA INFOGA_EFTER=N> ... </REDIGERA>  (N=0 inserts at the top of the file)
    // Insert before: <REDIGERA INFOGA_FÖRE=N> ... </REDIGERA>  (or ASCII INFOGA_FORE)
    // Insertions add new content — e.g. a brand-new function — without removing any existing lines.
    private const string EditTagName = "REDIGERA";
    private const string ReplaceKeyword = "RAD";
    private const string InsertAfterKeyword = "INFOGA_EFTER";

    /// <summary>
    /// Maximum characters of file/PDF content injected into a single LLM prompt. Without a cap,
    /// a large document (e.g. a full PDF book) gets embedded whole into the tool-result prompt;
    /// once that prompt exceeds the model's context window, llama-cli silently truncates or
    /// returns an empty completion instead of an answer. 200 000 chars (~50k tokens) leaves
    /// generous headroom for the tools system prompt and the model's own answer within the
    /// Gemma 4 model's 256K-token context window (see Gemma4CliOptions.ContextSize).
    /// </summary>
    private const int MaxInjectedContentChars = 200_000;
    private const string InsertBeforeKeyword = "INFOGA_FÖRE";
    private const string InsertBeforeKeywordAscii = "INFOGA_FORE";
    private static readonly Regex EditBlockRegex = new(
        $@"<{EditTagName}\s+(RAD|INFOGA_EFTER|INFOGA_F(?:Ö|O)RE)=(\d+)(?:-(\d+))?\s*>(.*?)</{EditTagName}>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fallback for TryFindAgentCommand: some models narrate their intent to use a tool instead of
    // writing the command alone on its own line as instructed, e.g.
    // `I will use the "/läs-pdf report.pdf Sammanfatta innehållet" command.` — this recognises a
    // command quoted inline (straight or curly quotes) so a well-formed request isn't dropped.
    private static readonly Regex QuotedCommandRegex = new(
        "[\"“]\\s*(/\\S[^\"“”\r\n]*)\\s*[\"”]",
        RegexOptions.Compiled);

    /// <inheritdoc/>
    public string BaseDirectory { get; private set; }

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
    public void SetBaseDirectory(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
            throw new ArgumentNullException(nameof(baseDirectory));

        var fullPath = Path.GetFullPath(baseDirectory);
        Directory.CreateDirectory(fullPath);
        BaseDirectory = fullPath;
    }

    /// <inheritdoc/>
    public async Task<FileAgentResult> SaveUploadedFileAsync(string filename, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(filename))
            return FileAgentResult.Failure("Ange ett filnamn.");

        var path = GetSafePath(filename);
        if (path is null)
            return FileAgentResult.Failure($"Ogiltigt filnamn: \"{filename}\".");

        await using (var fileStream = File.Create(path))
        {
            await content.CopyToAsync(fileStream);
        }

        return FileAgentResult.Success(FileAgentResultType.FileCreated, $"✓ Fil uppladdad: {Path.GetFileName(path)}");
    }

    /// <inheritdoc/>
    public bool IsCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        var t = input.TrimStart();
        return t.StartsWith("/skapa ", Cmp) || t.Equals("/skapa", Cmp)
            || t.StartsWith("/fyll ", Cmp) || t.Equals("/fyll", Cmp)
            || t.StartsWith("/läs ", Cmp) || t.Equals("/läs", Cmp)
            || t.StartsWith("/las ", Cmp) || t.Equals("/las", Cmp)
            || t.StartsWith("/läs-pdf ", Cmp) || t.Equals("/läs-pdf", Cmp)
            || t.StartsWith("/las-pdf ", Cmp) || t.Equals("/las-pdf", Cmp)
            || t.StartsWith("/redigera ", Cmp) || t.Equals("/redigera", Cmp)
            || t.Equals("/lista", Cmp)
            || t.StartsWith("/lista ", Cmp);
    }

    /// <inheritdoc/>
    public async Task<FileAgentResult> ExecuteAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return FileAgentResult.Failure("Tomt kommando.");

        var trimmed = input.Trim();

        if (trimmed.StartsWith("/skapa ", Cmp))
            return await CreateFileAsync(trimmed["/skapa ".Length..].Trim());
        if (trimmed.Equals("/skapa", Cmp))
            return await CreateFileAsync(string.Empty);

        if (trimmed.StartsWith("/fyll ", Cmp))
            return await FillFileAsync(trimmed["/fyll ".Length..].Trim());
        if (trimmed.Equals("/fyll", Cmp))
            return await FillFileAsync(string.Empty);

        if (trimmed.StartsWith("/läs ", Cmp))
            return await ReadFileAsync(trimmed["/läs ".Length..].Trim());
        if (trimmed.Equals("/läs", Cmp))
            return await ReadFileAsync(string.Empty);

        if (trimmed.StartsWith("/las ", Cmp))
            return await ReadFileAsync(trimmed["/las ".Length..].Trim());
        if (trimmed.Equals("/las", Cmp))
            return await ReadFileAsync(string.Empty);

        if (trimmed.StartsWith("/läs-pdf ", Cmp))
            return await ReadPdfCommandAsync(trimmed["/läs-pdf ".Length..].Trim());
        if (trimmed.Equals("/läs-pdf", Cmp))
            return await ReadPdfCommandAsync(string.Empty);

        if (trimmed.StartsWith("/las-pdf ", Cmp))
            return await ReadPdfCommandAsync(trimmed["/las-pdf ".Length..].Trim());
        if (trimmed.Equals("/las-pdf", Cmp))
            return await ReadPdfCommandAsync(string.Empty);

        if (trimmed.StartsWith("/redigera ", Cmp))
            return await EditFileAsync(trimmed["/redigera ".Length..].Trim());
        if (trimmed.Equals("/redigera", Cmp))
            return await EditFileAsync(string.Empty);

        if (trimmed.Equals("/lista", Cmp) || trimmed.StartsWith("/lista ", Cmp))
            return await ListFilesAsync();

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

    private async Task<FileAgentResult> ReadFileAsync(string args)
    {
        // Format: <filename> <instruktion> — the instruction is required so the command
        // carries an explicit task for the agent instead of forwarding the raw file as the
        // entire prompt.
        var spaceIdx = args.IndexOf(' ');
        if (spaceIdx < 0)
            return FileAgentResult.Failure(
                "Ange filnamn och en instruktion. Exempel: /läs text.txt Sammanfatta innehållet.");

        var filename = args[..spaceIdx].Trim();
        var instruction = args[(spaceIdx + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(instruction))
            return FileAgentResult.Failure(
                "Ange en instruktion efter filnamnet. Exempel: /läs text.txt Sammanfatta innehållet.");

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

        var promptForLlm =
            $"Instruktion: {instruction}\n\n" +
            $"Filens innehåll ({Path.GetFileName(path)}):\n{TruncateForLlm(content)}";

        return FileAgentResult.ReadSuccess(
            $"✓ Fil läst: {Path.GetFileName(path)}",
            promptForLlm);
    }

    /// <inheritdoc/>
    public async Task<FileAgentResult> ReadFileRawAsync(string filename)
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
            TruncateForLlm(content));
    }

    /// <inheritdoc/>
    public Task<FileAgentResult> ReadPdfFileAsync(string filename)
    {
        var (path, error) = ResolvePdfPath(filename);
        if (error is not null)
            return Task.FromResult(error);

        var (content, extractError) = ExtractPdfText(path!);
        if (extractError is not null)
            return Task.FromResult(extractError);

        return Task.FromResult(FileAgentResult.ReadSuccess(
            $"✓ PDF läst: {Path.GetFileName(path)}",
            content!));
    }

    // ── /läs-pdf ──────────────────────────────────────────────────────────

    private Task<FileAgentResult> ReadPdfCommandAsync(string args)
    {
        // Format: <filename> <instruktion> — same shape as /läs, but the file content is
        // extracted from a PDF via UglyToad.PdfPig instead of read as plain text.
        var spaceIdx = args.IndexOf(' ');
        if (spaceIdx < 0)
            return Task.FromResult(FileAgentResult.Failure(
                "Ange filnamn och en instruktion. Exempel: /läs-pdf rapport.pdf Sammanfatta innehållet."));

        var filename = args[..spaceIdx].Trim();
        var instruction = args[(spaceIdx + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(instruction))
            return Task.FromResult(FileAgentResult.Failure(
                "Ange en instruktion efter filnamnet. Exempel: /läs-pdf rapport.pdf Sammanfatta innehållet."));

        var (path, error) = ResolvePdfPath(filename);
        if (error is not null)
            return Task.FromResult(error);

        var (content, extractError) = ExtractPdfText(path!);
        if (extractError is not null)
            return Task.FromResult(extractError);

        var promptForLlm =
            $"Instruktion: {instruction}\n\n" +
            $"PDF-filens innehåll ({Path.GetFileName(path)}):\n{content}";

        return Task.FromResult(FileAgentResult.ReadSuccess(
            $"✓ PDF läst: {Path.GetFileName(path)}",
            promptForLlm));
    }

    /// <summary>
    /// Validates <paramref name="filename"/> as a bare, existing <c>.pdf</c> file inside
    /// <see cref="BaseDirectory"/>. Returns the resolved path on success, or a
    /// <see cref="FileAgentResult"/> describing the validation failure.
    /// </summary>
    private (string? Path, FileAgentResult? Error) ResolvePdfPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return (null, FileAgentResult.Failure("Ange ett filnamn. Exempel: /läs-pdf rapport.pdf Sammanfatta innehållet."));

        var path = GetSafePath(filename);
        if (path is null)
            return (null, FileAgentResult.Failure($"Ogiltigt filnamn: \"{filename}\"."));

        if (!Path.GetExtension(path).Equals(".pdf", Cmp))
            return (null, FileAgentResult.Failure($"Filen är inte en PDF: {Path.GetFileName(path)}"));

        if (!File.Exists(path))
            return (null, FileAgentResult.Failure($"Filen hittades inte: {Path.GetFileName(path)}"));

        return (path, null);
    }

    /// <summary>
    /// Truncates <paramref name="content"/> to <see cref="MaxInjectedContentChars"/> at a word
    /// boundary, appending a note so the model (and user) knows the document was cut short
    /// instead of silently answering from a partial view of it.
    /// </summary>
    private static string TruncateForLlm(string content)
    {
        if (content.Length <= MaxInjectedContentChars)
            return content;

        var truncated = content[..MaxInjectedContentChars];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > MaxInjectedContentChars - 200)
            truncated = truncated[..lastSpace];

        return truncated +
            $"\n\n[OBS: Innehållet har trunkerats — visar de första {truncated.Length} av {content.Length} tecken.]";
    }

    /// <summary>
    /// Extracts all page text from the PDF at <paramref name="path"/> via UglyToad.PdfPig, joining
    /// pages with a <c>--- Page N ---</c> marker. Returns the extracted content on success, or a
    /// <see cref="FileAgentResult"/> describing why extraction failed (corrupt file, no text, etc.).
    /// </summary>
    private static (string? Content, FileAgentResult? Error) ExtractPdfText(string path)
    {
        try
        {
            using var document = PdfDocument.Open(path);
            var text = new StringBuilder();
            var hasAnyRealText = false;

            foreach (var page in document.GetPages())
            {
                text.AppendLine($"--- Page {page.Number} ---");
                text.AppendLine(page.Text);
                text.AppendLine();

                if (!string.IsNullOrWhiteSpace(page.Text))
                    hasAnyRealText = true;
            }

            // A PDF can "succeed" here with only the "--- Page N ---" markers and no actual page
            // text — e.g. when the document's text was flattened to vector outlines/curves at
            // export time (common for print-ready designs) instead of real embedded text objects.
            // Checking the joined content alone would miss this, since the markers make it
            // non-blank; the LLM would then silently get zero real information about the file.
            if (!hasAnyRealText)
                return (null, FileAgentResult.Failure(
                    $"Ingen text kunde extraheras ur PDF:en: {Path.GetFileName(path)}. " +
                    "Filen verkar sakna riktig inbäddad text (t.ex. skannade sidor eller text som " +
                    "konverterats till kurvor/bilder vid export), vilket kräver OCR som inte stöds ännu."));

            var content = text.ToString().Trim();
            return (TruncateForLlm(content), null);
        }
        catch (Exception ex)
        {
            return (null, FileAgentResult.Failure(
                $"Kunde inte läsa PDF:en {Path.GetFileName(path)}: {ex.Message}"));
        }
    }

    // ── /redigera ─────────────────────────────────────────────────────────

    private async Task<FileAgentResult> EditFileAsync(string args)
    {
        // Format: <filename> <instruktion> — same shape as /läs, but the LLM's reply is expected
        // to contain structured <REDIGERA RAD=...> blocks that get applied to the file
        // automatically instead of being shown as plain chat text.
        var spaceIdx = args.IndexOf(' ');
        if (spaceIdx < 0)
            return FileAgentResult.Failure(
                "Ange filnamn och en instruktion. Exempel: /redigera text.txt Rätta stavfelet på rad 3.");

        var filename = args[..spaceIdx].Trim();
        var instruction = args[(spaceIdx + 1)..].Trim();

        if (string.IsNullOrWhiteSpace(instruction))
            return FileAgentResult.Failure(
                "Ange en instruktion efter filnamnet. Exempel: /redigera text.txt Rätta stavfelet på rad 3.");

        var path = GetSafePath(filename);
        if (path is null)
            return FileAgentResult.Failure($"Ogiltigt filnamn: \"{filename}\".");

        if (!File.Exists(path))
            return FileAgentResult.Failure(
                $"Filen hittades inte: {Path.GetFileName(path)}\n" +
                $"Skapa den med: /skapa {filename}");

        var lines = await File.ReadAllLinesAsync(path);
        if (lines.Length == 0)
            return FileAgentResult.Failure($"Filen är tom: {Path.GetFileName(path)}");

        var numberedContent = string.Join(
            "\n",
            lines.Select((line, i) => $"{i + 1}: {line}"));

        var llmPrompt =
            $"Du ska föreslå ändringar för filen \"{filename}\".\n" +
            $"Uppgift: {instruction}\n\n" +
            $"Filens innehåll med radnummer (radnumren är bara referenser, skriv inte ut dem i svaret):\n" +
            $"{numberedContent}\n\n" +
            $"VIKTIGT: Svara ENDAST med de ändringar som behövs, som ett eller flera block. Det finns två typer av block:\n\n" +
            $"1) ERSÄTTA befintliga rader — använd när du ska ändra/rätta text som redan finns:\n" +
            $"<{EditTagName} RAD=radnummer>\n" +
            $"nytt innehåll för raden\n" +
            $"</{EditTagName}>\n" +
            $"Använd <{EditTagName} RAD=start-slut> (t.ex. <{EditTagName} RAD=5-7>) om flera på varandra följande rader ska ersättas med samma nya innehåll.\n\n" +
            $"2) INFOGA NY KOD — använd NÄR DU LÄGGER TILL något helt nytt (t.ex. en ny funktion/metod), " +
            $"eftersom den befintliga koden då INTE ska skrivas över eller tas bort:\n" +
            $"<{EditTagName} INFOGA_EFTER=radnummer>\n" +
            $"ny kod som ska läggas till\n" +
            $"</{EditTagName}>\n" +
            $"Detta infogar innehållet direkt efter angiven rad utan att ta bort något. Använd <{EditTagName} INFOGA_EFTER=0> för att infoga allra först i filen. " +
            $"Alternativt <{EditTagName} INFOGA_FÖRE=radnummer> (eller ASCII-varianten INFOGA_FORE) för att infoga direkt före angiven rad — använd t.ex. radnumret för " +
            $"en klass eller namespaces avslutande \"}}\" för att lägga till en ny metod sist i rätt klass/namespace, eller radnumret för en using-sats för att lägga till en ny using överst.\n\n" +
            $"Välj alltid rätt radnummer så att den nya koden hamnar innanför rätt klass/namespace/block i filen ovan — titta på klammerparenteserna {{ }} för att avgöra var ett block börjar och slutar. " +
            $"Radnumren avser filens innehåll ovan.\n" +
            $"Skriv inga förklaringar utanför {EditTagName}-blocken. Om inga ändringar behövs alls, svara utan några block.";

        return FileAgentResult.EditRequest(filename, llmPrompt);
    }

    // ── /lista ──────────────────────────────────────────────────────────────

    private Task<FileAgentResult> ListFilesAsync()
    {
        var files = Directory.GetFiles(BaseDirectory)
            .Select(Path.GetFileName)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToArray();

        var listing = files.Length == 0
            ? "Inga filer finns i agentkatalogen."
            : string.Join(", ", files);

        return Task.FromResult(FileAgentResult.ListSuccess($"✓ Filer: {listing}", listing));
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

    /// <inheritdoc/>
    public bool TryExtractLineEdits(string llmResponse, out IReadOnlyList<LineEdit> edits)
    {
        var found = new List<LineEdit>();

        if (!string.IsNullOrWhiteSpace(llmResponse))
        {
            // Each match branches into multiple continue/skip paths below, so this isn't a
            // simple filter+project that reads better as a LINQ chain.
#pragma warning disable S3267
            foreach (Match match in EditBlockRegex.Matches(llmResponse))
            {
                var keyword = match.Groups[1].Value;
                if (!int.TryParse(match.Groups[2].Value, out var anchorLine))
                    continue;

                var content = match.Groups[4].Value.Trim('\r', '\n');

                if (keyword.Equals(InsertAfterKeyword, Cmp))
                {
                    // anchorLine == 0 means "insert at the very top of the file".
                    if (anchorLine < 0) continue;
                    found.Add(LineEdit.InsertAfterLine(anchorLine, content));
                }
                else if (keyword.Equals(InsertBeforeKeyword, Cmp) || keyword.Equals(InsertBeforeKeywordAscii, Cmp))
                {
                    if (anchorLine < 1) continue;
                    found.Add(LineEdit.InsertBeforeLine(anchorLine, content));
                }
                else if (keyword.Equals(ReplaceKeyword, Cmp))
                {
                    if (anchorLine < 1) continue;

                    var endLine = match.Groups[3].Success && int.TryParse(match.Groups[3].Value, out var parsedEnd)
                        ? parsedEnd
                        : anchorLine;

                    if (endLine < anchorLine) continue;

                    found.Add(new LineEdit(anchorLine, endLine, content));
                }
            }
#pragma warning restore S3267
        }

        edits = found;
        return found.Count > 0;
    }

    /// <inheritdoc/>
    public async Task<FileAgentResult> ApplyLineEditsAsync(string filename, IReadOnlyList<LineEdit> edits)
    {
        if (edits is null || edits.Count == 0)
            return FileAgentResult.Failure("Inga ändringar att tillämpa.");

        var path = GetSafePath(filename);
        if (path is null)
            return FileAgentResult.Failure($"Ogiltigt filnamn: \"{filename}\".");

        if (!File.Exists(path))
            return FileAgentResult.Failure($"Filen hittades inte: {Path.GetFileName(path)}");

        var lines = (await File.ReadAllLinesAsync(path)).ToList();

        // Merge pure insertions that land at the exact same splice point into one edit so the
        // apply order between them is never ambiguous — content is concatenated in request order.
        var merged = MergeCoincidentInsertions(edits);

        // Normalize every edit into a (RangeStart, RangeEnd) pair in original 1-based line-number
        // space so replacements and insertions can be validated/sorted/overlap-checked uniformly.
        // Insertions normalize to an empty range (RangeEnd == RangeStart - 1) anchored exactly at
        // the splice boundary, so an insertion sitting right before/after a replaced block never
        // counts as overlapping it, while one landing strictly inside a replaced range does.
        var normalized = merged
            .Select(e => (Edit: e, Range: GetNormalizedRange(e)))
            .OrderBy(x => x.Range.Start)
            .ThenBy(x => x.Range.End)
            .ToList();

        for (var i = 0; i < normalized.Count; i++)
        {
            var (edit, range) = normalized[i];

            if (edit.Kind == LineEditKind.Replace)
            {
                if (range.Start < 1 || range.End < range.Start || range.End > lines.Count)
                    return FileAgentResult.Failure(
                        $"Ogiltigt radintervall {edit.StartLine}-{edit.EndLine} — filen \"{Path.GetFileName(path)}\" har {lines.Count} rader. Ingen ändring gjordes.");
            }
            else if (range.End < 0 || range.End > lines.Count)
            {
                return FileAgentResult.Failure(
                    $"Ogiltig infogningsposition ({DescribeEdit(edit)}) — filen \"{Path.GetFileName(path)}\" har {lines.Count} rader. Ingen ändring gjordes.");
            }

            if (i > 0 && range.Start <= normalized[i - 1].Range.End)
                return FileAgentResult.Failure(
                    $"Överlappande ändringar ({DescribeEdit(normalized[i - 1].Edit)} och {DescribeEdit(edit)}) — filen ändrades inte.");
        }

        // Apply from the bottom of the file upward so earlier edits' line numbers stay valid
        // even when a replacement/insertion has a different number of lines than the range it
        // affects. When an insertion sits exactly at the boundary of a replace edit (a tie on
        // RangeStart), the replace is applied first so the insertion lands relative to the new
        // (post-replace) content rather than being pushed aside by it.
        foreach (var (edit, _) in normalized
            .OrderByDescending(x => x.Range.Start)
            .ThenByDescending(x => x.Range.End))
        {
            var newLines = edit.NewContent.Replace("\r\n", "\n").Split('\n');

            if (edit.Kind == LineEditKind.Replace)
            {
                var index = edit.StartLine - 1;
                var count = edit.EndLine - edit.StartLine + 1;
                lines.RemoveRange(index, count);
                lines.InsertRange(index, newLines);
            }
            else
            {
                lines.InsertRange(GetInsertSpliceIndex(edit), newLines);
            }
        }

        await File.WriteAllLinesAsync(path, lines);

        var summary = string.Join(", ", normalized.Select(x => DescribeEdit(x.Edit)));

        return FileAgentResult.Success(
            FileAgentResultType.FileEdited,
            $"✓ Fil redigerad: {Path.GetFileName(path)} ({summary})");
    }

    /// <summary>
    /// Returns the 0-based index into the file's line list where an insertion edit's content
    /// should be spliced in (no lines removed). Only valid for <see cref="LineEditKind.InsertAfter"/>
    /// and <see cref="LineEditKind.InsertBefore"/> edits.
    /// </summary>
    private static int GetInsertSpliceIndex(LineEdit edit) => edit.Kind switch
    {
        LineEditKind.InsertAfter => edit.StartLine,
        LineEditKind.InsertBefore => edit.StartLine - 1,
        _ => throw new InvalidOperationException("GetInsertSpliceIndex kan bara användas för infogningar.")
    };

    /// <summary>
    /// Normalizes any <see cref="LineEdit"/> into an inclusive (Start, End) range in original
    /// 1-based line-number space. Replacements map directly to their own range; insertions map
    /// to an empty range anchored at their splice point (see <see cref="GetInsertSpliceIndex"/>).
    /// </summary>
    private static (int Start, int End) GetNormalizedRange(LineEdit edit) => edit.Kind switch
    {
        LineEditKind.Replace => (edit.StartLine, edit.EndLine),
        LineEditKind.InsertAfter => (edit.StartLine + 1, edit.StartLine),
        LineEditKind.InsertBefore => (edit.StartLine, edit.StartLine - 1),
        _ => throw new ArgumentOutOfRangeException(nameof(edit))
    };

    /// <summary>
    /// Combines any insertion edits that target the exact same splice point into a single edit
    /// (content concatenated in the original order) so applying them has no ambiguous ordering.
    /// Replace edits pass through unchanged.
    /// </summary>
    private static List<LineEdit> MergeCoincidentInsertions(IReadOnlyList<LineEdit> edits)
    {
        var result = new List<LineEdit>();
        var insertGroups = new Dictionary<int, List<LineEdit>>();
        var groupOrder = new List<int>();

        foreach (var edit in edits)
        {
            if (edit.Kind == LineEditKind.Replace)
            {
                result.Add(edit);
                continue;
            }

            var spliceIndex = GetInsertSpliceIndex(edit);
            if (!insertGroups.TryGetValue(spliceIndex, out var group))
            {
                group = new List<LineEdit>();
                insertGroups[spliceIndex] = group;
                groupOrder.Add(spliceIndex);
            }
            group.Add(edit);
        }

        foreach (var spliceIndex in groupOrder)
        {
            var group = insertGroups[spliceIndex];
            result.Add(group.Count == 1
                ? group[0]
                : LineEdit.InsertAfterLine(spliceIndex, string.Join("\n", group.Select(e => e.NewContent))));
        }

        return result;
    }

    /// <summary>
    /// Produces a short, human-readable description of an edit for summaries/error messages,
    /// e.g. "rad 5", "rad 5-7", "infogat efter rad 4", or "infogat före rad 9".
    /// </summary>
    private static string DescribeEdit(LineEdit edit) => edit.Kind switch
    {
        LineEditKind.Replace => edit.StartLine == edit.EndLine
            ? $"rad {edit.StartLine}"
            : $"rad {edit.StartLine}-{edit.EndLine}",
        LineEditKind.InsertAfter => $"infogat efter rad {edit.StartLine}",
        LineEditKind.InsertBefore => $"infogat före rad {edit.StartLine}",
        _ => throw new ArgumentOutOfRangeException(nameof(edit))
    };

    /// <inheritdoc/>
    public string StripEditMarkers(string llmResponse) =>
        EditBlockRegex.Replace(llmResponse, string.Empty).Trim();

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> GetToolDescriptions() => new Dictionary<string, string>
    {
        ["/läs <filnamn> <instruktion>"] = "Läser innehållet i en fil i agentkatalogen och skickar det tillsammans med instruktionen till dig, t.ex. \"/läs text.txt Sammanfatta innehållet.\"",
        ["/läs-pdf <filnamn> <instruktion>"] = "Extraherar texten ur en PDF-fil i agentkatalogen och skickar den tillsammans med instruktionen till dig, t.ex. \"/läs-pdf rapport.pdf Sammanfatta innehållet och föreslå en åtgärd.\"",
        ["/skapa <filnamn>"] = "Skapar en ny, tom fil med angivet namn i agentkatalogen.",
        ["/fyll <filnamn> <beskrivning>"] = "Genererar innehåll utifrån beskrivningen och sparar det i filen.",
        ["/redigera <filnamn> <instruktion>"] = "Läser en fil med radnummer, ber dig ange exakt vilka rader som ska ersättas och med vad (eller var ny kod, t.ex. en ny funktion, ska infogas utan att skriva över något), och uppdaterar sedan filen automatiskt utifrån ditt svar.",
        ["/lista"] = "Listar alla filer som just nu finns i agentkatalogen."
    };

    /// <inheritdoc/>
    public string BuildToolsSystemPrompt()
    {
        var lines = new List<string>
        {
            "Du har tillgång till följande filverktyg. Om du behöver använda ett verktyg för att kunna " +
            "svara på frågan, skriv kommandot EXAKT enligt formatet nedan på en egen rad i ditt svar " +
            "— och skriv inget annat på den raden. Du får då verktygets resultat och kan därefter ge ditt slutgiltiga svar."
        };
        lines.AddRange(GetToolDescriptions().Select(kv => $"- {kv.Key} : {kv.Value}"));
        lines.Add("Om du inte behöver något verktyg för att svara, skriv bara ditt svar direkt i vanlig text.");
        return string.Join("\n", lines);
    }

    /// <inheritdoc/>
    public bool TryFindAgentCommand(string llmResponse, out string command)
    {
        command = string.Empty;
        if (string.IsNullOrWhiteSpace(llmResponse)) return false;

        foreach (var rawLine in llmResponse.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (IsCommand(line))
            {
                command = line;
                return true;
            }
        }

        // Fallback: the model explained its intent to use a tool in prose instead of writing the
        // command alone on its own line (e.g. `I will use the "/läs-pdf report.pdf Sammanfatta
        // innehållet" command.`) — recognise a quoted command so the request still executes.
        foreach (Match match in QuotedCommandRegex.Matches(llmResponse))
        {
            var candidate = match.Groups[1].Value.Trim();
            if (IsCommand(candidate))
            {
                command = candidate;
                return true;
            }
        }

        return false;
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

        // Ensure the resolved path stays within BaseDirectory. Compare against the directory plus a
        // trailing separator so a sibling directory whose name merely shares the prefix (e.g. base
        // "C:\data" vs "C:\data-evil") is not treated as inside it.
        var baseWithSeparator = Path.TrimEndingDirectorySeparator(BaseDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase))
            return null;

        return fullPath;
    }
}
