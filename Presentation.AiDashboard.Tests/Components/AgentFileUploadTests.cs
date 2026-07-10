using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Services.FileAgent;
using AgentFileUpload = AiDashboard.Components.Shared.AgentFileUpload;

namespace Presentation.AiDashboard.Tests.Components;

/// <summary>
/// Tests for <see cref="AgentFileUpload"/>: the chat composer's attach button that saves an
/// uploaded file (e.g. a PDF) into the active workspace via <see cref="IFileAgentService"/> so
/// the LLM can subsequently read it. Uses a real <see cref="FileAgentService"/> rooted at a
/// per-test temp directory, mirroring the convention in <see cref="Services.Tests.FileAgent.FileAgentServiceTests"/>.
/// </summary>
public sealed class AgentFileUploadTests : TestContext, IDisposable
{
    private readonly string _tempDir;

    public AgentFileUploadTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AgentFileUploadTests_" + Guid.NewGuid());
        Services.AddSingleton<IFileAgentService>(new FileAgentService(_tempDir));
    }

    public new void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Renders_HiddenInputLinkedToLabel()
    {
        var cut = RenderComponent<AgentFileUpload>();

        var input = cut.Find("input.agent-upload-input");
        var label = cut.Find("label.agent-upload-btn");

        Assert.Equal("file", input.GetAttribute("type"));
        Assert.Equal(input.GetAttribute("id"), label.GetAttribute("for"));
    }

    [Fact]
    public void DarkMode_HasDarkThemeClass()
    {
        var cut = RenderComponent<AgentFileUpload>(parameters => parameters
            .Add(p => p.DarkMode, true));

        Assert.Contains("theme-dark", cut.Find(".agent-upload").ClassName);
    }

    [Fact]
    public void LightMode_HasLightThemeClass()
    {
        var cut = RenderComponent<AgentFileUpload>(parameters => parameters
            .Add(p => p.DarkMode, false));

        Assert.Contains("theme-light", cut.Find(".agent-upload").ClassName);
    }

    [Fact]
    public void Disabled_DisablesInput()
    {
        var cut = RenderComponent<AgentFileUpload>(parameters => parameters
            .Add(p => p.Disabled, true));

        Assert.NotNull(cut.Find("input.agent-upload-input").GetAttribute("disabled"));
        Assert.Contains("disabled", cut.Find("label.agent-upload-btn").ClassName);
    }

    [Fact]
    public void MultipleInstances_HaveUniqueIds()
    {
        var cut1 = RenderComponent<AgentFileUpload>();
        var cut2 = RenderComponent<AgentFileUpload>();

        var id1 = cut1.Find("input.agent-upload-input").GetAttribute("id");
        var id2 = cut2.Find("input.agent-upload-input").GetAttribute("id");

        Assert.NotEqual(id1, id2);
    }

    [Fact]
    public void UploadValidFile_SavesToWorkspaceAndInvokesOnUploaded()
    {
        string? uploadedName = null;
        var cut = RenderComponent<AgentFileUpload>(parameters => parameters
            .Add(p => p.OnUploaded, name => uploadedName = name));

        var inputFile = cut.FindComponent<InputFile>();
        var file = InputFileContent.CreateFromBinary("hello pdf content"u8.ToArray(), "report.pdf");
        inputFile.UploadFiles(file);

        Assert.Equal("report.pdf", uploadedName);
        Assert.True(File.Exists(Path.Combine(_tempDir, "report.pdf")));
    }

    [Fact]
    public void UploadOversizedFile_InvokesOnErrorAndDoesNotSave()
    {
        string? errorMessage = null;
        var cut = RenderComponent<AgentFileUpload>(parameters => parameters
            .Add(p => p.MaxFileSizeBytes, 10)
            .Add(p => p.OnError, msg => errorMessage = msg));

        var inputFile = cut.FindComponent<InputFile>();
        var file = InputFileContent.CreateFromBinary(new byte[100], "toobig.pdf");
        inputFile.UploadFiles(file);

        Assert.NotNull(errorMessage);
        Assert.Contains("för stor", errorMessage);
        Assert.False(File.Exists(Path.Combine(_tempDir, "toobig.pdf")));
    }

    [Theory]
    [InlineData("photo.png")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("photo.gif")]
    [InlineData("photo.webp")]
    [InlineData("photo.bmp")]
    public void UploadImageFile_DefaultAcceptedTypes_SavesToWorkspace(string filename)
    {
        string? uploadedName = null;
        var cut = RenderComponent<AgentFileUpload>(parameters => parameters
            .Add(p => p.OnUploaded, name => uploadedName = name));

        var inputFile = cut.FindComponent<InputFile>();
        var file = InputFileContent.CreateFromBinary(new byte[] { 1, 2, 3, 4 }, filename);
        inputFile.UploadFiles(file);

        Assert.Equal(filename, uploadedName);
        Assert.True(File.Exists(Path.Combine(_tempDir, filename)));
    }

    [Fact]
    public void UploadDisallowedFileType_InvokesOnErrorAndDoesNotSave()
    {
        // Browsers only enforce the `accept` attribute for the file-picker dialog, not for a
        // drag-and-drop — so a dropped file of an unsupported type must still be rejected here.
        string? errorMessage = null;
        var cut = RenderComponent<AgentFileUpload>(parameters => parameters
            .Add(p => p.OnError, msg => errorMessage = msg));

        var inputFile = cut.FindComponent<InputFile>();
        var file = InputFileContent.CreateFromBinary(new byte[] { 1, 2, 3 }, "not-supported.exe");
        inputFile.UploadFiles(file);

        Assert.NotNull(errorMessage);
        Assert.Contains("stöds inte", errorMessage);
        Assert.False(File.Exists(Path.Combine(_tempDir, "not-supported.exe")));
    }

    [Fact]
    public void DropZoneSelector_NotSet_NoJsInteropAttempted()
    {
        // No JSInterop configured in this TestContext at all — if OnAfterRenderAsync tried to
        // invoke JS with the default null DropZoneSelector, this would throw/fail the render.
        var cut = RenderComponent<AgentFileUpload>();

        Assert.NotNull(cut.Find(".agent-upload"));
    }

    [Fact]
    public void DropZoneSelector_Set_JsInteropFailureIsSwallowed()
    {
        // No JSInterop configured for "fileDropZone.initializePersistent" — bUnit's strict mode
        // would throw for an unconfigured call, but AgentFileUpload wraps it in a try/catch, the
        // same pattern used by FileDropZone.razor, so the component still renders successfully.
        var cut = RenderComponent<AgentFileUpload>(parameters => parameters
            .Add(p => p.DropZoneSelector, ".oa-chat"));

        Assert.NotNull(cut.Find(".agent-upload"));
    }
}
