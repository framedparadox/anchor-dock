using DockGx.Models;
using DockGx.Services;
using Xunit;

namespace DockGx.Tests.Services;

public class DockItemFactoryTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "DockGxTests_" + Guid.NewGuid().ToString("N"));

    public DockItemFactoryTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Classify_recognizes_an_existing_directory_as_a_folder()
    {
        Assert.Equal(DockItemKind.Folder, DockItemFactory.Classify(_tempDir));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com/path?query=1")]
    public void Classify_recognizes_http_and_https_urls_as_web_links(string url)
    {
        Assert.Equal(DockItemKind.WebLink, DockItemFactory.Classify(url));
    }

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData("notepad.exe")]
    [InlineData(@"C:\Tools\run.bat")]
    [InlineData("script.cmd")]
    [InlineData("legacy.com")]
    [InlineData(@"C:\Users\Public\Desktop\App.lnk")]
    public void Classify_recognizes_launchable_extensions_as_applications(string target)
    {
        Assert.Equal(DockItemKind.Application, DockItemFactory.Classify(target));
    }

    [Theory]
    [InlineData(@"C:\Users\Test\notes.txt")]
    [InlineData("readme")]
    public void Classify_falls_back_to_file_for_anything_else(string target)
    {
        Assert.Equal(DockItemKind.File, DockItemFactory.Classify(target));
    }

    [Fact]
    public void SuggestName_uses_the_host_for_a_web_link()
    {
        Assert.Equal("github.com",
            DockItemFactory.SuggestName("https://github.com/microsoft/WinUI-Gallery"));
    }

    [Fact]
    public void SuggestName_uses_the_filename_without_extension_for_an_app_path()
    {
        Assert.Equal("explorer", DockItemFactory.SuggestName(@"C:\Windows\explorer.exe"));
    }

    [Fact]
    public void SuggestName_trims_a_trailing_separator_for_a_folder_path()
    {
        Assert.Equal("Documents", DockItemFactory.SuggestName(@"C:\Users\Test\Documents\"));
    }

    [Fact]
    public void SuggestName_strips_the_extension_from_a_bare_filename()
    {
        Assert.Equal("readme", DockItemFactory.SuggestName("readme.txt"));
    }
}
