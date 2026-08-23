using Anchor.Models;
using Windows.Graphics;

namespace Anchor.Services;

/// <summary>
/// Pure geometry for multi-monitor dock snap and shared-edge detection.
/// Kept free of <c>DisplayArea</c> / window handles so unit tests can cover the rules without a
/// live WinUI host.
/// </summary>
public static class DockPlacement
{
    /// <summary>Max distance (physical px) from a work-area edge that still counts as a snap.</summary>
    public const int SnapThreshold = 16;

    /// <summary>How far past an outer edge to probe for a neighboring monitor.</summary>
    public const int NeighborProbeOffset = 8;

    /// <summary>
    /// Max gap (physical px) between two outer bounds that still counts as an abutting / shared edge.
    /// Covers small virtual-desktop gaps and DPI rounding without treating distant monitors as neighbors.
    /// </summary>
    public const int AbutTolerance = 16;

    /// <summary>
    /// Decide whether a window dropped at <paramref name="x"/>,<paramref name="y"/> should snap
    /// to an edge of <paramref name="work"/>.
    /// </summary>
    /// <remarks>
    /// The caller must pass the work area of the display under the window's center (same rule as
    /// layout). Distances are measured only against that rect, so a dock on monitor B cannot
    /// spuriously snap using monitor A's edges. Slight overhang past an edge (negative distance
    /// within the threshold) still snaps; a center outside the work area does not.
    /// </remarks>
    /// <returns>The edge to snap to, or <c>null</c> to stay floating.</returns>
    public static DockEdge? DecideSnapEdge(
        RectInt32 work, int x, int y, int width, int height, int threshold = SnapThreshold)
    {
        if (work.Width <= 0 || work.Height <= 0 || width <= 0 || height <= 0 || threshold < 0)
            return null;

        int cx = x + width / 2;
        int cy = y + height / 2;
        // Center must belong to this work area; otherwise the wrong monitor was supplied.
        if (cx < work.X || cx >= work.X + work.Width || cy < work.Y || cy >= work.Y + work.Height)
            return null;

        int dLeft = x - work.X;
        int dTop = y - work.Y;
        int dRight = work.X + work.Width - (x + width);
        int dBottom = work.Y + work.Height - (y + height);

        // Nearest edge among those within threshold (allow a few px of overhang past the edge).
        DockEdge? best = null;
        int bestDist = int.MaxValue;
        Consider(DockEdge.Left, dLeft);
        Consider(DockEdge.Top, dTop);
        Consider(DockEdge.Right, dRight);
        Consider(DockEdge.Bottom, dBottom);
        return best;

        void Consider(DockEdge edge, int distance)
        {
            if (distance > threshold)
                return;
            // Far past the edge (e.g. mostly on another monitor) is not a snap.
            if (distance < -threshold)
                return;
            int score = Math.Abs(distance);
            // Tie-break matches the previous EndDragSnap ternary: Bottom, then Top, then Left,
            // then Right when distances are equal.
            if (score < bestDist || (score == bestDist && PreferOver(edge, best)))
            {
                bestDist = score;
                best = edge;
            }
        }

        static bool PreferOver(DockEdge candidate, DockEdge? current) =>
            current is null || TieRank(candidate) < TieRank(current.Value);

        // Lower rank wins on equal distance (matches prior ternary: Bottom > Top > Left > Right).
        static int TieRank(DockEdge e) => e switch
        {
            DockEdge.Bottom => 0,
            DockEdge.Top => 1,
            DockEdge.Left => 2,
            _ => 3,
        };
    }

    /// <summary>
    /// True when another monitor sits immediately beyond <paramref name="edge"/> of
    /// <paramref name="outer"/> (a shared / interior edge). Auto-hide must not slide the dock
    /// into that neighbor.
    /// </summary>
    /// <param name="outer">Full bounds of the dock's own monitor.</param>
    /// <param name="edge">The snapped edge.</param>
    /// <param name="shown">Current on-screen dock rect (used to place probes along the strip).</param>
    /// <param name="allOuters">Outer bounds of every display (including <paramref name="outer"/>).</param>
    public static bool EdgeHasNeighbor(
        RectInt32 outer,
        DockEdge edge,
        RectInt32 shown,
        IReadOnlyList<RectInt32> allOuters,
        int probeOffset = NeighborProbeOffset,
        int abutTolerance = AbutTolerance)
    {
        if (outer.Width <= 0 || outer.Height <= 0)
            return false;

        if (allOuters.Count > 0 && AnyAbuttingNeighbor(outer, edge, allOuters, abutTolerance))
            return true;

        // Point probes as a fallback when the abut list is empty or incomplete (e.g. caller could
        // not enumerate displays). Probing mid-span and near both ends covers docks parked in a
        // corner of a shared edge.
        foreach (var probe in NeighborProbes(outer, edge, shown, probeOffset))
        {
            foreach (var other in allOuters)
            {
                if (RectsEqual(other, outer))
                    continue;
                if (ContainsInclusive(other, probe))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Point probes just past <paramref name="outer"/> along <paramref name="edge"/>, at the
    /// dock's mid-span and near each end of the strip.
    /// </summary>
    public static IEnumerable<PointInt32> NeighborProbes(
        RectInt32 outer, DockEdge edge, RectInt32 shown, int probeOffset = NeighborProbeOffset)
    {
        if (probeOffset < 1)
            probeOffset = 1;

        bool horizontal = edge is DockEdge.Top or DockEdge.Bottom;
        int spanStart = horizontal ? shown.X : shown.Y;
        int spanLength = horizontal ? Math.Max(1, shown.Width) : Math.Max(1, shown.Height);
        int mid = spanStart + spanLength / 2;
        // Keep end probes inside the strip so a dock along only part of the edge still detects
        // a neighbor that only covers that span.
        int nearStart = spanStart + Math.Min(8, spanLength / 4);
        int nearEnd = spanStart + spanLength - 1 - Math.Min(8, spanLength / 4);
        if (nearEnd < nearStart)
            nearEnd = mid;

        int[] along = mid == nearStart && mid == nearEnd
            ? new[] { mid }
            : new[] { nearStart, mid, nearEnd };

        foreach (int a in along.Distinct())
        {
            yield return edge switch
            {
                DockEdge.Bottom => new PointInt32(a, outer.Y + outer.Height + probeOffset),
                DockEdge.Top => new PointInt32(a, outer.Y - probeOffset),
                DockEdge.Left => new PointInt32(outer.X - probeOffset, a),
                DockEdge.Right => new PointInt32(outer.X + outer.Width + probeOffset, a),
                _ => new PointInt32(a, outer.Y + outer.Height + probeOffset),
            };
        }
    }

    /// <summary>
    /// True when any other outer rect abuts <paramref name="outer"/> on <paramref name="edge"/>
    /// with overlapping span along that edge.
    /// </summary>
    public static bool AnyAbuttingNeighbor(
        RectInt32 outer, DockEdge edge, IReadOnlyList<RectInt32> allOuters, int tolerance = AbutTolerance)
    {
        foreach (var other in allOuters)
        {
            if (RectsEqual(other, outer) || other.Width <= 0 || other.Height <= 0)
                continue;
            if (AbutsOnEdge(outer, other, edge, tolerance))
                return true;
        }
        return false;
    }

    /// <summary>Whether <paramref name="other"/> shares <paramref name="edge"/> of <paramref name="self"/>.</summary>
    public static bool AbutsOnEdge(RectInt32 self, RectInt32 other, DockEdge edge, int tolerance = AbutTolerance)
    {
        bool spansOverlapY = RangesOverlap(self.Y, self.Height, other.Y, other.Height);
        bool spansOverlapX = RangesOverlap(self.X, self.Width, other.X, other.Width);

        return edge switch
        {
            DockEdge.Right =>
                spansOverlapY && Math.Abs(other.X - (self.X + self.Width)) <= tolerance,
            DockEdge.Left =>
                spansOverlapY && Math.Abs((other.X + other.Width) - self.X) <= tolerance,
            DockEdge.Bottom =>
                spansOverlapX && Math.Abs(other.Y - (self.Y + self.Height)) <= tolerance,
            DockEdge.Top =>
                spansOverlapX && Math.Abs((other.Y + other.Height) - self.Y) <= tolerance,
            _ => false,
        };
    }

    private static bool RangesOverlap(int aStart, int aLen, int bStart, int bLen)
    {
        int aEnd = aStart + aLen;
        int bEnd = bStart + bLen;
        return aStart < bEnd && bStart < aEnd;
    }

    private static bool ContainsInclusive(RectInt32 r, PointInt32 p) =>
        p.X >= r.X && p.X < r.X + r.Width && p.Y >= r.Y && p.Y < r.Y + r.Height;

    private static bool RectsEqual(RectInt32 a, RectInt32 b) =>
        a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height;
}
