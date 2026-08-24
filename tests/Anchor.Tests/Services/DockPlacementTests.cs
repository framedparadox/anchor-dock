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

    [Fact]
    public void EdgeHasNeighbor_false_when_neighbor_only_covers_the_other_end_of_the_edge()
    {
        // Secondary sits to the right of the top half; the dock is parked on the bottom-right
        // of a tall primary — that stretch is a true outer edge, not a shared one.
        var tallPrimary = new RectInt32(0, 0, 1920, 2160);
        var topSecondary = new RectInt32(1920, 0, 1920, 1080);
        var shown = new RectInt32(1920 - 48, 1800, 48, 200);
        Assert.False(DockPlacement.EdgeHasNeighbor(
            tallPrimary, DockEdge.Right, shown, new[] { tallPrimary, topSecondary }));
    }

    [Fact]
    public void EdgeHasNeighbor_true_when_the_dock_span_overlaps_the_neighbor()
    {
        var tallPrimary = new RectInt32(0, 0, 1920, 2160);
        var topSecondary = new RectInt32(1920, 0, 1920, 1080);
        var shown = new RectInt32(1920 - 48, 400, 48, 200);
        Assert.True(DockPlacement.EdgeHasNeighbor(
            tallPrimary, DockEdge.Right, shown, new[] { tallPrimary, topSecondary }));
    }

    // ---- Shared-edge hide: stay on this monitor, notch only ----------------

    [Fact]
    public void SharedEdgeHiddenRect_on_the_right_stays_on_the_snapped_monitor()
    {
        var shown = new RectInt32(1920 - 48, 400, 48, 200);
        var hidden = DockPlacement.SharedEdgeHiddenRect(
            PrimaryOuter, shown, DockEdge.Right, notchLength: 40, notchThickness: 10);

        Assert.True(DockPlacement.RectInside(PrimaryOuter, hidden));
        Assert.Equal(PrimaryOuter.X + PrimaryOuter.Width - 10, hidden.X);
        Assert.Equal(10, hidden.Width);
        Assert.Equal(40, hidden.Height);
        // Must not occupy any pixel of the secondary (x >= 1920).
        Assert.True(hidden.X + hidden.Width <= SecondaryOuter.X);
    }

    [Fact]
    public void SharedEdgeHiddenRect_on_the_left_of_the_secondary_stays_on_the_secondary()
    {
        var shown = new RectInt32(1920, 400, 48, 200);
        var hidden = DockPlacement.SharedEdgeHiddenRect(
            SecondaryOuter, shown, DockEdge.Left, notchLength: 40, notchThickness: 10);

        Assert.True(DockPlacement.RectInside(SecondaryOuter, hidden));
        Assert.Equal(SecondaryOuter.X, hidden.X);
        Assert.True(hidden.X >= SecondaryOuter.X);
        Assert.True(hidden.X + hidden.Width <= SecondaryOuter.X + SecondaryOuter.Width);
    }

    [Fact]
    public void SharedEdgeHiddenRect_on_a_stacked_bottom_edge_stays_on_the_upper_monitor()
    {
        var below = new RectInt32(0, 1080, 1920, 1080);
        var shown = new RectInt32(760, 1080 - 48, 400, 48);
        var hidden = DockPlacement.SharedEdgeHiddenRect(
            PrimaryOuter, shown, DockEdge.Bottom, notchLength: 62, notchThickness: 10);

        Assert.True(DockPlacement.RectInside(PrimaryOuter, hidden));
        Assert.Equal(PrimaryOuter.Y + PrimaryOuter.Height - 10, hidden.Y);
        Assert.True(hidden.Y + hidden.Height <= below.Y);
    }

    [Fact]
    public void LerpRect_from_shown_to_shared_hidden_never_enters_the_neighbor()
    {
        var shown = new RectInt32(1920 - 48, 400, 48, 200);
        var hidden = DockPlacement.SharedEdgeHiddenRect(
            PrimaryOuter, shown, DockEdge.Right, notchLength: 40, notchThickness: 10);

        for (int i = 0; i <= 20; i++)
        {
            var frame = DockPlacement.LerpRect(shown, hidden, i / 20.0);
            Assert.True(DockPlacement.RectInside(PrimaryOuter, frame),
                $"Frame at t={i / 20.0} left the snapped monitor.");
            Assert.True(frame.X + frame.Width <= SecondaryOuter.X,
                $"Frame at t={i / 20.0} crossed onto the secondary.");
        }
    }

    // ---- A real portrait-left arrangement ----------------------------------
    //
    // The layout these rules were verified against on hardware: a 1080x1920 portrait screen to
    // the LEFT of a 1920x1080 primary, nudged 5px down (Windows lets monitors sit at any offset,
    // and the arrangement UI rarely lands on an exact one). It is worth pinning as a fixture
    // because it exercises two things a tidy side-by-side pair cannot: a small vertical offset
    // that must still read as abutting, and an edge that is shared along part of its length and a
    // true outer edge along the rest — the portrait screen is 1920 tall while the primary it
    // meets is only 1080, so the bottom 840px of its right edge face nothing at all.

    private static readonly RectInt32 PortraitLeftOuter = new(-1080, 5, 1080, 1920);
    private static readonly RectInt32 LandscapeOuter = new(0, 0, 1920, 1080);

    private static readonly IReadOnlyList<RectInt32> PortraitLeftLayout =
        new[] { LandscapeOuter, PortraitLeftOuter };

    [Fact]
    public void PortraitLeft_landscape_left_edge_is_shared_despite_the_offset()
    {
        // Dock on the landscape screen's left edge: the portrait screen is right behind it, so
        // hiding must not slide the window that way.
        var shown = new RectInt32(0, 481, 193, 52);
        Assert.True(DockPlacement.EdgeHasNeighbor(
            LandscapeOuter, DockEdge.Left, shown, PortraitLeftLayout));
    }

    [Fact]
    public void PortraitLeft_portrait_right_edge_is_shared_where_the_landscape_screen_is()
    {
        // High up the portrait screen's right edge, the landscape screen is behind it.
        var shown = new RectInt32(-193, 500, 193, 52);
        Assert.True(DockPlacement.EdgeHasNeighbor(
            PortraitLeftOuter, DockEdge.Right, shown, PortraitLeftLayout));
    }

    [Fact]
    public void PortraitLeft_portrait_right_edge_is_outer_below_the_landscape_screen()
    {
        // The same edge, lower down: past the bottom of the landscape screen there is nothing
        // out there, so the dock is free to slide off it like any other outer edge.
        var shown = new RectInt32(-193, 1500, 193, 52);
        Assert.False(DockPlacement.EdgeHasNeighbor(
            PortraitLeftOuter, DockEdge.Right, shown, PortraitLeftLayout));
    }

    [Fact]
    public void PortraitLeft_shared_hide_keeps_every_frame_off_the_neighbour()
    {
        // The notch, and every frame of the collapse into it, stays on the portrait screen —
        // never a pixel at x >= 0, which is the landscape screen.
        var shown = new RectInt32(-193, 500, 193, 52);
        var hidden = DockPlacement.SharedEdgeHiddenRect(
            PortraitLeftOuter, shown, DockEdge.Right, notchLength: 40, notchThickness: 10);

        Assert.True(DockPlacement.RectInside(PortraitLeftOuter, hidden));
        Assert.Equal(-10, hidden.X);
        Assert.Equal(10, hidden.Width);

        for (int i = 0; i <= 20; i++)
        {
            var frame = DockPlacement.LerpRect(shown, hidden, i / 20.0);
            Assert.True(frame.X + frame.Width <= LandscapeOuter.X,
                $"Frame at t={i / 20.0} crossed onto the landscape screen.");
        }
    }

    [Fact]
    public void PortraitLeft_drop_near_the_shared_edge_snaps_on_the_screen_it_was_dropped_on()
    {
        // Dropped just inside the portrait screen's right edge: it must snap there, using that
        // screen's work area — not jump to the landscape screen whose edge is the same line.
        var portraitWork = new RectInt32(-1080, 5, 1080, 1872);
        var edge = DockPlacement.DecideSnapEdge(portraitWork, x: -191, y: 800, width: 193, height: 52);
        Assert.Equal(DockEdge.Right, edge);
    }
}
