using Anchor.Models;
using Anchor.Services;
using Windows.Graphics;
using Xunit;

namespace Anchor.Tests.Services;

/// <summary>
/// Multi-monitor snap and shared-edge rules. These are the regressions for "dock hides on
/// screen 1 but appears on the adjacent screen" and for drop-snap using the wrong work area.
/// </summary>
public class DockPlacementTests
{
    // Primary-like work area at the origin; secondary to its right (side-by-side).
    private static readonly RectInt32 PrimaryWork = new(0, 0, 1920, 1040);
    private static readonly RectInt32 PrimaryOuter = new(0, 0, 1920, 1080);
    private static readonly RectInt32 SecondaryOuter = new(1920, 0, 1920, 1080);
    private static readonly RectInt32 SecondaryWork = new(1920, 0, 1920, 1040);

    private static readonly IReadOnlyList<RectInt32> SideBySide =
        new[] { PrimaryOuter, SecondaryOuter };

    // ---- DecideSnapEdge ----------------------------------------------------

    [Fact]
    public void DecideSnapEdge_snaps_when_flush_to_bottom_of_this_work_area()
    {
        // 400×48 strip sitting on the bottom edge of the primary work area.
        var edge = DockPlacement.DecideSnapEdge(PrimaryWork, x: 760, y: 1040 - 48, width: 400, height: 48);
        Assert.Equal(DockEdge.Bottom, edge);
    }

    [Fact]
    public void DecideSnapEdge_floats_when_dropped_away_from_every_edge()
    {
        var edge = DockPlacement.DecideSnapEdge(PrimaryWork, x: 800, y: 400, width: 400, height: 48);
        Assert.Null(edge);
    }

    [Fact]
    public void DecideSnapEdge_still_snaps_with_a_few_pixels_of_overhang()
    {
        // Slightly past the right work edge — still a snap, not a float.
        var edge = DockPlacement.DecideSnapEdge(
            PrimaryWork, x: 1920 - 48 + 4, y: 400, width: 48, height: 400);
        Assert.Equal(DockEdge.Right, edge);
    }

    [Fact]
    public void DecideSnapEdge_rejects_wrong_monitor_work_area()
    {
        // Classic bug: dock is fully on the secondary screen, but distances were measured
        // against the primary work area. Center is not in PrimaryWork → must not snap.
        var edge = DockPlacement.DecideSnapEdge(
            PrimaryWork, x: 1920 + 100, y: 400, width: 400, height: 48);
        Assert.Null(edge);
    }

    [Fact]
    public void DecideSnapEdge_snaps_on_secondary_using_secondary_work_area()
    {
        var edge = DockPlacement.DecideSnapEdge(
            SecondaryWork, x: 1920 + 100, y: 1040 - 48, width: 400, height: 48);
        Assert.Equal(DockEdge.Bottom, edge);
    }

    [Fact]
    public void DecideSnapEdge_does_not_snap_when_far_past_an_edge()
    {
        // Center still inside primary (x=1880, w=60 → cx=1910), but dRight = -20 exceeds the
        // overhang allowance — must not treat that as a right-edge snap.
        var edge = DockPlacement.DecideSnapEdge(
            PrimaryWork, x: 1920 - 40, y: 400, width: 60, height: 48, threshold: 16);
        Assert.Null(edge);
    }

    [Fact]
    public void DecideSnapEdge_bottom_wins_equal_distance_tie()
    {
        // Square work area, dock sized so left and bottom distances are both 0.
        var work = new RectInt32(0, 0, 200, 200);
        var edge = DockPlacement.DecideSnapEdge(work, x: 0, y: 200 - 50, width: 50, height: 50);
        Assert.Equal(DockEdge.Bottom, edge);
    }

    // ---- Shared / interior edges -------------------------------------------

    [Fact]
    public void EdgeHasNeighbor_true_on_primary_right_when_secondary_abuts()
    {
        var shown = new RectInt32(1920 - 48, 400, 48, 200);
        Assert.True(DockPlacement.EdgeHasNeighbor(
            PrimaryOuter, DockEdge.Right, shown, SideBySide));
    }

    [Fact]
    public void EdgeHasNeighbor_false_on_primary_left_outer_edge()
    {
        var shown = new RectInt32(0, 400, 48, 200);
        Assert.False(DockPlacement.EdgeHasNeighbor(
            PrimaryOuter, DockEdge.Left, shown, SideBySide));
    }

    [Fact]
    public void EdgeHasNeighbor_false_on_secondary_right_outer_edge()
    {
        var shown = new RectInt32(1920 + 1920 - 48, 400, 48, 200);
        Assert.False(DockPlacement.EdgeHasNeighbor(
            SecondaryOuter, DockEdge.Right, shown, SideBySide));
    }

    [Fact]
    public void EdgeHasNeighbor_true_on_secondary_left_shared_with_primary()
    {
        var shown = new RectInt32(1920, 400, 48, 200);
        Assert.True(DockPlacement.EdgeHasNeighbor(
            SecondaryOuter, DockEdge.Left, shown, SideBySide));
    }

    [Fact]
    public void EdgeHasNeighbor_tolerates_small_virtual_desktop_gap()
    {
        // 8px gap between monitors — still a shared edge for auto-hide purposes.
        var gapped = new RectInt32[]
        {
            PrimaryOuter,
            new(1920 + 8, 0, 1920, 1080),
        };
        var shown = new RectInt32(1920 - 48, 400, 48, 200);
        Assert.True(DockPlacement.EdgeHasNeighbor(
            PrimaryOuter, DockEdge.Right, shown, gapped));
    }

    [Fact]
    public void AbutsOnEdge_requires_overlapping_span()
    {
        // Secondary is to the right but only covers the top half; dock on primary bottom-right
        // still abuts horizontally on X, and Y ranges overlap partially — true.
        var tallPrimary = new RectInt32(0, 0, 1920, 2160);
        var topSecondary = new RectInt32(1920, 0, 1920, 1080);
        Assert.True(DockPlacement.AbutsOnEdge(
            tallPrimary, topSecondary, DockEdge.Right));

        // Secondary far below with no Y overlap → not a right-edge neighbor.
        var below = new RectInt32(1920, 3000, 1920, 1080);
        Assert.False(DockPlacement.AbutsOnEdge(PrimaryOuter, below, DockEdge.Right));
    }

    [Fact]
    public void NeighborProbes_cover_mid_and_ends_of_the_strip()
    {
        var shown = new RectInt32(100, 0, 400, 48);
        var probes = DockPlacement.NeighborProbes(PrimaryOuter, DockEdge.Top, shown).ToList();
        Assert.True(probes.Count >= 2);
        Assert.All(probes, p => Assert.Equal(PrimaryOuter.Y - DockPlacement.NeighborProbeOffset, p.Y));
        Assert.Contains(probes, p => p.X > shown.X && p.X < shown.X + shown.Width);
    }
}
