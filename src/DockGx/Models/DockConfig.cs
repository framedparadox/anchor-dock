namespace DockGx.Models;

/// <summary>Which screen edge the dock is snapped to.</summary>
public enum DockEdge
{
    Bottom,
    Top,
    Left,
    Right,
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

    /// <summary>Start DockGx automatically when the user signs in (per-user Run key).</summary>
    public bool LaunchAtStartup { get; set; }

    /// <summary>
    /// True once the default items have been seeded (first run). Prevents re-seeding after the
    /// user has intentionally emptied the dock — an empty dock then persists and shows the
    /// "+ Add New" affordance instead of springing the defaults back.
    /// </summary>
    public bool Seeded { get; set; }
}
