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
    /// <paramref name="outer"/> <em>behind this dock</em> (a shared / interior edge). Auto-hide
    /// must not slide the dock into that neighbor — it collapses to a notch that stays on
    /// <paramref name="outer"/> instead.
    /// </summary>
    /// <param name="outer">Full bounds of the dock's own monitor.</param>
    /// <param name="edge">The snapped edge.</param>
    /// <param name="shown">Current on-screen dock rect (the span that has to actually meet a neighbor).</param>
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

        if (allOuters.Count > 0 && AnyAbuttingNeighbor(outer, edge, shown, allOuters, abutTolerance))
            return true;

        // Point probes as a fallback when the abut list is empty or incomplete (e.g. caller could
        // not enumerate displays). Probing mid-span and near both ends covers docks parked in a
        // corner of a shared edge — and, because the probes follow <paramref name="shown"/>, a
        // neighbor that only covers the other end of the monitor is ignored.
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
    /// <em>behind <paramref name="shown"/></em>. A neighbor that only covers a different stretch
    /// of the same monitor edge does not count: the dock there is on a true outer edge and can
    /// hide into the void.
    /// </summary>
    public static bool AnyAbuttingNeighbor(
        RectInt32 outer,
        DockEdge edge,
        RectInt32 shown,
        IReadOnlyList<RectInt32> allOuters,
        int tolerance = AbutTolerance)
    {
        foreach (var other in allOuters)
        {
            if (RectsEqual(other, outer) || other.Width <= 0 || other.Height <= 0)
                continue;
            if (AbutsOnEdge(outer, other, edge, tolerance) && NeighborCoversDock(shown, other, edge))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether <paramref name="neighbor"/> actually sits behind the dock along
    /// <paramref name="edge"/>, not merely somewhere else on that monitor's edge.
    /// </summary>
    public static bool NeighborCoversDock(RectInt32 shown, RectInt32 neighbor, DockEdge edge) =>
        edge is DockEdge.Left or DockEdge.Right
            ? RangesOverlap(shown.Y, shown.Height, neighbor.Y, neighbor.Height)
            : RangesOverlap(shown.X, shown.Width, neighbor.X, neighbor.Width);

    /// <summary>
    /// Window rectangle for a dock hidden on a shared edge: entirely inside
    /// <paramref name="outer"/>, flush to that edge, occupying only the notch band on this
    /// monitor. The HWND never crosses into a neighbor — which is the whole point: sliding the
    /// full window past a shared boundary would paint it on the screen next door (the compositor
    /// treats the virtual desktop as one surface, and a system acrylic backdrop cannot be clipped
    /// with <c>SetWindowRgn</c>).
    /// </summary>
    public static RectInt32 SharedEdgeHiddenRect(
        RectInt32 outer, RectInt32 shown, DockEdge edge, int notchLength, int notchThickness)
    {
        if (outer.Width <= 0 || outer.Height <= 0)
            return shown;

        bool verticalEdge = edge is DockEdge.Left or DockEdge.Right;
        int maxThick = verticalEdge ? outer.Width : outer.Height;
        int maxLen = verticalEdge ? outer.Height : outer.Width;
        notchThickness = Math.Clamp(notchThickness, 1, Math.Max(1, maxThick));
        notchLength = Math.Clamp(notchLength, 1, Math.Max(1, maxLen));

        if (verticalEdge)
        {
            int len = Math.Min(notchLength, Math.Max(1, shown.Height));
            int y = Math.Clamp(
                shown.Y + (shown.Height - len) / 2,
                outer.Y,
                outer.Y + outer.Height - len);
            int x = edge == DockEdge.Right
                ? outer.X + outer.Width - notchThickness
                : outer.X;
            return new RectInt32(x, y, notchThickness, len);
        }
        else
        {
            int len = Math.Min(notchLength, Math.Max(1, shown.Width));
            int x = Math.Clamp(
                shown.X + (shown.Width - len) / 2,
                outer.X,
                outer.X + outer.Width - len);
            int y = edge == DockEdge.Bottom
                ? outer.Y + outer.Height - notchThickness
                : outer.Y;
            return new RectInt32(x, y, len, notchThickness);
        }
    }

    /// <summary>True when <paramref name="inner"/> lies entirely inside <paramref name="outer"/>.</summary>
    public static bool RectInside(RectInt32 outer, RectInt32 inner) =>
        inner.Width > 0 && inner.Height > 0 &&
        inner.X >= outer.X &&
        inner.Y >= outer.Y &&
        inner.X + inner.Width <= outer.X + outer.Width &&
        inner.Y + inner.Height <= outer.Y + outer.Height;

    /// <summary>
    /// Linear interpolation of two rects. A dock collapsing toward a shared-edge notch uses this
    /// so every in-flight frame stays on the same side of the boundary: the lerp of two rects
    /// inside a monitor is still inside that monitor.
    /// </summary>
    public static RectInt32 LerpRect(RectInt32 from, RectInt32 to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new RectInt32(
            Lerp(from.X, to.X, t),
            Lerp(from.Y, to.Y, t),
            Math.Max(1, Lerp(from.Width, to.Width, t)),
            Math.Max(1, Lerp(from.Height, to.Height, t)));
    }

    private static int Lerp(int a, int b, double t) => (int)Math.Round(a + (b - a) * t);

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
