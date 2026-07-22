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
    /// When true the dock is snapped flush to <see cref="Edge"/> and auto-hides behind it,
    /// revealing on cursor approach. When false the dock floats freely at (<see cref="FreeX"/>,
    /// <see cref="FreeY"/>) and stays fully visible.
    /// </summary>
    public bool Snapped { get; set; }

    public DockEdge Edge { get; set; } = DockEdge.Bottom;

    /// <summary>Free-floating top-left position in physical pixels (used when not snapped).</summary>
    public int? FreeX { get; set; }
    public int? FreeY { get; set; }
}
