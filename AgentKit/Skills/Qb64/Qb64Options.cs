namespace AgentKit.Skills.Qb64;

/// <summary>
/// Options for <see cref="Qb64ToolService"/>. The LLM only ever supplies a bare .bas filename
/// that is resolved inside the active workspace — the compiler path and argument shape come
/// exclusively from these options, which hosts map from their own configuration system.
/// </summary>
public class Qb64Options
{
    /// <summary>
    /// Full path to qb64.exe (download from https://qb64.com/ or QB64 Phoenix Edition).
    /// Leave empty to disable the QB64 tool — the commands are then never offered to the LLM.
    /// </summary>
    public string CompilerPath { get; set; } = string.Empty;

    /// <summary>
    /// Argument template passed to the compiler. <c>{source}</c> is replaced with the full path
    /// of the .bas file and <c>{output}</c> with the full path of the .exe to produce.
    /// The default uses QB64's headless mode: <c>-x</c> compiles without opening the IDE and
    /// writes compiler output (including errors) to the console.
    /// </summary>
    public string CompilerArguments { get; set; } = "-x \"{source}\" -o \"{output}\"";

    /// <summary>
    /// Compile timeout in milliseconds. QB64 invokes a C++ backend under the hood, and the very
    /// first compile on a machine can take considerably longer than subsequent ones.
    /// Default: 180000 (3 minutes).
    /// </summary>
    public int CompileTimeoutMs { get; set; } = 180_000;

    /// <summary>
    /// Run timeout in milliseconds for the compiled program; the process tree is killed when
    /// exceeded (e.g. a program stuck waiting for keyboard input). Output captured up to that
    /// point is still returned to the LLM. Default: 30000.
    /// </summary>
    public int RunTimeoutMs { get; set; } = 30_000;

    /// <summary>Maximum characters of compiler/program output returned to the LLM. Default: 4000.</summary>
    public int MaxOutputLength { get; set; } = 4000;
}
