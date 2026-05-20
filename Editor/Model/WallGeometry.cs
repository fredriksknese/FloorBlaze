namespace FloorPlan.Editor.Model;

// Computes mitered wall polygons so wall bands fuse cleanly at corners,
// regardless of the angle. The 4 corners of each wall's polygon shift along
// the corner bisector at every shared node, so adjacent walls share an edge.
public static class WallGeometry
{
    // If a miter would extend further than this many wall-thicknesses past the
    // node, fall back to a perpendicular butt cap on that corner. Without this,
    // very acute angles (e.g. 10°) would shoot the miter point off to infinity.
    private const double MITER_LIMIT = 4.0;

    // Returns the 4 polygon corners (world coordinates) for a wall.
    // CornersAtNode labels corners by "left/right" relative to the local
    // away-from-node direction. At N2 that direction is reversed, so
    // left@N2 lies on the same physical face as right@N1 (and vice versa).
    // The polygon walks one physical face from N1→N2, then back along the
    // other face from N2→N1: [left@N1, right@N2, left@N2, right@N1].
    public static Pt[] ComputePolygon(Wall w, WallNodeSequence seq)
    {
        var (leftN1, rightN1) = CornersAtNode(w, w.LeftNode, w.RightNode, seq);
        var (leftN2, rightN2) = CornersAtNode(w, w.RightNode, w.LeftNode, seq);
        return new[] { leftN1, rightN2, leftN2, rightN1 };
    }

    private static (Pt left, Pt right) CornersAtNode(
        Wall w, WallNode n, WallNode other, WallNodeSequence seq)
    {
        Pt np = new(n.X, n.Y);
        double dx = other.X - np.X, dy = other.Y - np.Y;
        double dlen = Math.Sqrt(dx * dx + dy * dy);
        if (dlen < 1e-6) return (np, np);

        // direction along the wall AWAY from this node
        Pt d = new(dx / dlen, dy / dlen);
        // CCW perpendicular (the "left" of d)
        Pt nL = new(-d.Y, d.X);
        double half = w.Thickness / 2.0;
        // butt-cap fallback positions (used when no neighbor or miter-limit exceeded)
        Pt leftButt = new(np.X + half * nL.X, np.Y + half * nL.Y);
        Pt rightButt = new(np.X - half * nL.X, np.Y - half * nL.Y);

        // Gather all walls sharing this node (excluding self and room dividers,
        // which render as dashed centerlines and have no body to miter against).
        var dirs = new List<(Pt dir, double half, double angle, bool isSelf)>
        {
            (d, half, Math.Atan2(d.Y, d.X), true)
        };
        foreach (var w2 in seq.Walls)
        {
            if (w2 == w) continue;
            if (w2.Type == WallType.RoomDivider) continue;
            WallNode? otherEnd =
                w2.LeftNode.Id == n.Id ? w2.RightNode :
                w2.RightNode.Id == n.Id ? w2.LeftNode : null;
            if (otherEnd == null) continue;
            double ex = otherEnd.X - np.X, ey = otherEnd.Y - np.Y;
            double elen = Math.Sqrt(ex * ex + ey * ey);
            if (elen < 1e-6) continue;
            Pt ed = new(ex / elen, ey / elen);
            dirs.Add((ed, w2.Thickness / 2.0, Math.Atan2(ed.Y, ed.X), false));
        }

        if (dirs.Count <= 1) return (leftButt, rightButt);

        dirs.Sort((a, b) => a.angle.CompareTo(b.angle));
        int self = dirs.FindIndex(x => x.isSelf);
        int count = dirs.Count;
        var ccw = dirs[(self + 1) % count];
        var cw = dirs[(self - 1 + count) % count];

        // Left corner = W.leftFace ∩ ccwNeighbor.rightFace
        Pt nLccw = new(-ccw.dir.Y, ccw.dir.X);
        Pt ccwRightFaceOrigin = new(np.X - ccw.half * nLccw.X, np.Y - ccw.half * nLccw.Y);
        Pt leftCorner = TryIntersect(leftButt, d, ccwRightFaceOrigin, ccw.dir, out var lc) ? lc : leftButt;
        if (Geometry.EuclideanDistance(leftCorner.X, np.X, leftCorner.Y, np.Y)
            > MITER_LIMIT * w.Thickness) leftCorner = leftButt;

        // Right corner = W.rightFace ∩ cwNeighbor.leftFace
        Pt nLcw = new(-cw.dir.Y, cw.dir.X);
        Pt cwLeftFaceOrigin = new(np.X + cw.half * nLcw.X, np.Y + cw.half * nLcw.Y);
        Pt rightCorner = TryIntersect(rightButt, d, cwLeftFaceOrigin, cw.dir, out var rc) ? rc : rightButt;
        if (Geometry.EuclideanDistance(rightCorner.X, np.X, rightCorner.Y, np.Y)
            > MITER_LIMIT * w.Thickness) rightCorner = rightButt;

        return (leftCorner, rightCorner);
    }

    // Intersect two infinite lines (O1 + s·D1) and (O2 + t·D2). Returns false if
    // they are parallel (det≈0); caller falls back to a butt cap in that case.
    private static bool TryIntersect(Pt o1, Pt d1, Pt o2, Pt d2, out Pt result)
    {
        double det = d1.X * (-d2.Y) - d1.Y * (-d2.X);
        if (Math.Abs(det) < 1e-9) { result = default; return false; }
        double dx = o2.X - o1.X, dy = o2.Y - o1.Y;
        double s = (dx * (-d2.Y) - dy * (-d2.X)) / det;
        result = new Pt(o1.X + s * d1.X, o1.Y + s * d1.Y);
        return true;
    }

    // For a junction with 3 or more walls, each wall's polygon is cut along the
    // bisector between itself and its angular neighbor. Those cuts leave a
    // hollow polygon at the centre of the junction (e.g. a triangle for a T).
    // Returns the vertices of that hollow polygon (in CCW angular order) so it
    // can be filled with the wall colour. Returns null if no fill is needed.
    public static Pt[]? ComputeNodeFill(WallNode n, WallNodeSequence seq)
    {
        Pt np = new(n.X, n.Y);
        var walls = new List<(Pt dir, double half, double angle, double thickness)>();
        foreach (var w in seq.Walls)
        {
            if (w.Type == WallType.RoomDivider) continue;
            WallNode? other =
                w.LeftNode.Id == n.Id ? w.RightNode :
                w.RightNode.Id == n.Id ? w.LeftNode : null;
            if (other == null) continue;
            double dx = other.X - np.X, dy = other.Y - np.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) continue;
            Pt d = new(dx / len, dy / len);
            walls.Add((d, w.Thickness / 2.0, Math.Atan2(d.Y, d.X), w.Thickness));
        }
        if (walls.Count < 3) return null;
        walls.Sort((a, b) => a.angle.CompareTo(b.angle));

        // The miter between wall i and wall (i+1) (its angular CCW neighbour) is
        // exactly the same intersection ComputePolygon uses for wall i's left
        // corner at this node. Reusing that intersection here keeps the fill
        // polygon and the wall polygons sharing identical vertices.
        var verts = new Pt[walls.Count];
        for (int i = 0; i < walls.Count; i++)
        {
            var w = walls[i];
            var nxt = walls[(i + 1) % walls.Count];
            Pt nLw = new(-w.dir.Y, w.dir.X);
            Pt nLnxt = new(-nxt.dir.Y, nxt.dir.X);
            Pt wLeftOrigin = new(np.X + w.half * nLw.X, np.Y + w.half * nLw.Y);
            Pt nxtRightOrigin = new(np.X - nxt.half * nLnxt.X, np.Y - nxt.half * nLnxt.Y);
            if (TryIntersect(wLeftOrigin, w.dir, nxtRightOrigin, nxt.dir, out var p) &&
                Geometry.EuclideanDistance(p.X, np.X, p.Y, np.Y) <= MITER_LIMIT * w.thickness)
            {
                verts[i] = p;
            }
            else
            {
                // parallel face lines (collinear walls) — same fallback the wall
                // polygon uses: the wall's left butt point on the node side
                verts[i] = wLeftOrigin;
            }
        }
        return verts;
    }
}
