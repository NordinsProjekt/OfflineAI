using System.ComponentModel;
using AgentKit.Skills.Utility;
using Microsoft.SemanticKernel;

namespace Services.AgentTools;

/// <summary>
/// Semantic Kernel plugin that exposes <see cref="IUtilityToolsService"/> operations
/// (current time, current date, and calling a named pre-configured API endpoint) as
/// <c>[KernelFunction]</c> methods so Gemma 4 can call them automatically during a
/// tool-calling session, alongside <see cref="BuiltInFileTools"/>.
/// <para>
/// Register with the kernel before calling <c>IGemma4AgentService.ChatWithToolsAsync</c>:
/// <code>
/// var kernel = gemma4Service.CreateKernel();
/// kernel.Plugins.AddFromObject(new BuiltInUtilityTools(utilityTools), "utility");
/// string answer = await gemma4Service.ChatWithToolsAsync(userMessage, kernel);
/// </code>
/// </para>
/// <para>
/// The <c>call_api</c> function only accepts an endpoint <em>name</em> — never a raw URL — so
/// the model is limited to the HTTP destinations the user has explicitly configured in
/// <c>AppConfiguration.AgentTools.Endpoints</c>.
/// </para>
/// </summary>
public sealed class BuiltInUtilityTools(IUtilityToolsService utilityTools)
{
    private readonly IUtilityToolsService _utilityTools =
        utilityTools ?? throw new ArgumentNullException(nameof(utilityTools));

    /// <summary>Returns the current time.</summary>
    [KernelFunction("get_current_time")]
    [Description("Returns the current wall-clock time.")]
    public async Task<string> GetCurrentTimeAsync()
    {
        var result = await _utilityTools.ExecuteAsync("/tid");
        return result.InjectedContext ?? result.Message;
    }

    /// <summary>Returns today's date.</summary>
    [KernelFunction("get_current_date")]
    [Description("Returns today's date.")]
    public async Task<string> GetCurrentDateAsync()
    {
        var result = await _utilityTools.ExecuteAsync("/datum");
        return result.InjectedContext ?? result.Message;
    }

    /// <summary>Calls a named, pre-configured HTTP API endpoint.</summary>
    [KernelFunction("call_api")]
    [Description("Calls a named, pre-configured HTTP API endpoint and returns its response. You may only pass the endpoint name (never a raw URL) — only endpoints the user has configured are reachable.")]
    public async Task<string> CallApiAsync(
        [Description("The exact name of a configured API endpoint")]
        string endpointName,
        [Description("What you want to know or accomplish with this call, forwarded to the endpoint if it supports it")]
        string instruction = "")
    {
        var result = await _utilityTools.CallNamedApiAsync(endpointName, instruction);
        return result.InjectedContext ?? result.Message;
    }
}
