using System.Text.Json;
using System.Text.Json.Serialization;
using DockGx.Models;
using Xunit;

namespace DockGx.Tests.Models;

public class DockConfigTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Defaults_match_the_first_run_experience()
    {
        var cfg = new DockConfig();

        Assert.Empty(cfg.Items);
        Assert.False(cfg.Snapped);
        Assert.Equal(DockEdge.Bottom, cfg.Edge);
        Assert.Null(cfg.FreeX);
        Assert.Null(cfg.FreeY);
        Assert.True(cfg.AutoHide);
        Assert.False(cfg.LaunchAtStartup);
        Assert.True(cfg.AlwaysOnTop);
        Assert.Equal(DockTheme.Dark, cfg.Theme);
        Assert.False(cfg.Seeded);
    }

    [Fact]
    public void Config_saved_before_the_theme_option_still_loads_as_Dark()
    {
        // An existing config file has no "Theme" field. It must deserialize to Dark so the app
        // keeps its original dark-taskbar look for users who upgrade.
        var cfg = JsonSerializer.Deserialize<DockConfig>("{ \"Seeded\": true }", Options);

        Assert.NotNull(cfg);
        Assert.Equal(DockTheme.Dark, cfg!.Theme);
    }

    [Theory]
    [InlineData(DockTheme.Light)]
    [InlineData(DockTheme.Dark)]
    [InlineData(DockTheme.System)]
    public void Theme_round_trips_through_json_as_a_string(DockTheme theme)
    {
        var json = JsonSerializer.Serialize(new DockConfig { Theme = theme }, Options);
        Assert.Contains($"\"{theme}\"", json); // stored by name, not ordinal

        var loaded = JsonSerializer.Deserialize<DockConfig>(json, Options);
        Assert.Equal(theme, loaded!.Theme);
    }
}
