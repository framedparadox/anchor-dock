using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Windows.UI;
using WinRT;

namespace DockGx.Services;

/// <summary>
/// Applies a Windows 11 taskbar-style acrylic ("glass") backdrop to a window.
///
/// Two things make this look like the real taskbar rather than a generic WinUI window:
///  1. The backdrop is forced <b>always active</b> (<c>IsInputActive = true</c>). A dock is
///     never the foreground window, and by default WinUI collapses acrylic to a flat
///     fallback color when its window is deactivated. Forcing active keeps the glass alive.
///  2. A hand-tuned tint/luminosity recipe per theme, layered on the system "Base" acrylic,
///     so it reads like the shell's own material and follows the Windows light/dark theme.
/// </summary>
public sealed class AcrylicBackdropManager : IDisposable
{
    private readonly Window _window;
    private DesktopAcrylicController? _controller;
    private SystemBackdropConfiguration? _config;
    private FrameworkElement? _themeRoot;
    private bool _disposed;

    public AcrylicBackdropManager(Window window) => _window = window;

    /// <summary>Recipe used in dark mode. Tunable to match the Win11 dark taskbar exactly.</summary>
    public AcrylicRecipe Dark { get; set; } = new(
        Tint: Rgb(0x1C, 0x1C, 0x1C),
        TintOpacity: 0.55,
        LuminosityOpacity: 0.90,
        Fallback: Rgb(0x2C, 0x2C, 0x2C));

    /// <summary>Recipe used in light mode.</summary>
    public AcrylicRecipe Light { get; set; } = new(
        Tint: Rgb(0xF2, 0xF2, 0xF2),
        TintOpacity: 0.55,
        LuminosityOpacity: 0.90,
        Fallback: Rgb(0xF3, 0xF3, 0xF3));

    /// <returns>true if acrylic was applied; false if the OS/GPU can't support it.</returns>
    public bool TryApply()
    {
        if (!DesktopAcrylicController.IsSupported())
            return false;

        _config = new SystemBackdropConfiguration
        {
            IsInputActive = true, // never fall back to solid when unfocused
        };

        _themeRoot = _window.Content as FrameworkElement;
        if (_themeRoot is not null)
            _themeRoot.ActualThemeChanged += OnThemeChanged;

        _controller = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Base,
        };

        UpdateTheme(); // sets config.Theme + applies the matching recipe

        _controller.SetSystemBackdropConfiguration(_config);
        _controller.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
        return true;
    }

    private void OnThemeChanged(FrameworkElement sender, object args) => UpdateTheme();

    private void UpdateTheme()
    {
        if (_controller is null || _config is null)
            return;

        var theme = _themeRoot?.ActualTheme ?? ElementTheme.Dark;
        bool dark = theme != ElementTheme.Light;

        _config.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;

        var r = dark ? Dark : Light;
        _controller.TintColor = r.Tint;
        _controller.TintOpacity = (float)r.TintOpacity;
        _controller.LuminosityOpacity = (float)r.LuminosityOpacity;
        _controller.FallbackColor = r.Fallback;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_themeRoot is not null)
            _themeRoot.ActualThemeChanged -= OnThemeChanged;

        _controller?.Dispose();
        _controller = null;
        _config = null;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}

/// <summary>A tunable acrylic material recipe.</summary>
/// <param name="Tint">Base tint color.</param>
/// <param name="TintOpacity">How strongly the tint color is applied (0..1).</param>
/// <param name="LuminosityOpacity">Frostiness — the taskbar leans high here (0..1).</param>
/// <param name="Fallback">Solid color used when composition acrylic is unavailable.</param>
public readonly record struct AcrylicRecipe(
    Color Tint,
    double TintOpacity,
    double LuminosityOpacity,
    Color Fallback);
