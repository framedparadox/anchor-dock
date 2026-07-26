namespace Anchor.Models;

/// <summary>Which screen edge the dock is snapped to.</summary>
public enum DockEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// The app's colour theme. <see cref="System"/> follows the current Windows light/dark setting;
/// <see cref="Light"/> and <see cref="Dark"/> pin it regardless. A High Contrast accessibility
/// theme always overrides this so the shell's high-contrast colours come through.
/// </summary>
public enum DockTheme
{
    Light,
    Dark,
    System,
}

/// <summary>Everything that persists between runs: the items and dock settings.</summary>
public sealed class DockConfig
{
    public List<DockItem> Items { get; set; } = new();

    /// <summary>
    /// When true the dock is snapped flush to <see cref="Edge"/>. If <see cref="AutoHide"/> is
    /// also on it slides behind that edge and reveals on cursor approach; otherwise it stays
    /// pinned flush and fully visible. When false the dock floats freely at (<see cref="FreeX"/>,
    /// <see cref="FreeY"/>).
    /// </summary>
    public bool Snapped { get; set; }

    public DockEdge Edge { get; set; } = DockEdge.Bottom;

    /// <summary>
    /// The dock's last top-left position in physical pixels. Kept up to date whether snapped or
    /// floating: when floating it is the free position; when snapped it records where the user
    /// dropped the dock so it re-appears at that spot (and on that monitor) rather than snapping
    /// back to the screen centre. Null only before the dock has ever been placed.
    /// </summary>
    public int? FreeX { get; set; }
    public int? FreeY { get; set; }

    // ---- General settings -------------------------------------------------

    /// <summary>When snapped, hide the dock behind the edge and reveal on hover.</summary>
    public bool AutoHide { get; set; } = true;

    /// <summary>Start Anchor automatically when the user signs in (per-user Run key).</summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>
    /// Keep the dock above other windows while it is <b>floating</b> (not snapped). When snapped
    /// the dock is always topmost regardless, so the auto-hide reveal works over other windows.
    /// </summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>
    /// The app's colour theme. Defaults to <see cref="DockTheme.Dark"/> so it reads like the
    /// Windows 11 dark taskbar (and so existing configs without this field keep that look).
    /// </summary>
    public DockTheme Theme { get; set; } = DockTheme.Dark;

    /// <summary>
    /// When the dock is snapped to the <b>left or right</b> edge, arrange its icons vertically
    /// instead of horizontally. Top/bottom snapping and floating always stay horizontal. Off by
    /// default (the dock is horizontal everywhere).
    /// </summary>
    public bool VerticalWhenSideSnapped { get; set; }

    /// <summary>
    /// True once the default items have been seeded (first run). Prevents re-seeding after the
    /// user has intentionally emptied the dock — an empty dock then persists and shows the
    /// "+ Add New" affordance instead of springing the defaults back.
    /// </summary>
    public bool Seeded { get; set; }
}
