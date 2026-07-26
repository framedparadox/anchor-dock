using System.Text.Json;
using System.Text.Json.Serialization;
using Anchor.Models;
using Xunit;

namespace Anchor.Tests.Models;

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
        Assert.False(cfg.VerticalWhenSideSnapped);
        Assert.False(cfg.Seeded);
        Assert.Equal(string.Empty, cfg.Language); // follow the Windows display language
        Assert.True(cfg.HotkeyEnabled);
        Assert.Equal("Ctrl+Alt+A", cfg.Hotkey);
    }

    [Fact]
    public void Config_saved_before_the_language_and_hotkey_options_still_loads()
    {
        // Upgrading users have a config file with neither field. The language must stay "follow
        // Windows" and — since JSON omission means "take the property initializer" — the default
        // shortcut comes along, so the feature is on for them without a re-save.
        var cfg = JsonSerializer.Deserialize<DockConfig>("{ \"Seeded\": true }", Options);

        Assert.NotNull(cfg);
        Assert.Equal(string.Empty, cfg!.Language);
        Assert.Equal("Ctrl+Alt+A", cfg.Hotkey);
        Assert.True(cfg.HotkeyEnabled);
    }

    [Fact]
    public void The_default_hotkey_string_is_one_the_gesture_parser_accepts()
    {
        // DockConfig stores the shortcut as text; if the two ever drift, Anchor would ship with
        // a shortcut that silently fails to register.
        Assert.True(HotkeyGesture.TryParse(new DockConfig().Hotkey, out var gesture));
        Assert.True(gesture.IsValid);
    }

    [Fact]
    public void An_unparseable_hotkey_does_not_stop_the_config_loading()
    {
        // Hand-edited nonsense degrades to "no shortcut", not to a lost dock.
        var cfg = JsonSerializer.Deserialize<DockConfig>("{ \"Hotkey\": \"Ctrl+Nonsense\" }", Options);

        Assert.NotNull(cfg);
        Assert.False(HotkeyGesture.TryParse(cfg!.Hotkey, out _));
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
