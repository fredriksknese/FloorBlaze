using FloorPlan.Editor.Model;

namespace FloorPlan.Editor;

public class Room
{
    public List<Pt> Polygon = new();
    public double AreaPx;
    public double AreaM2 => AreaPx / (Constants.METER * Constants.METER);

    public Pt Centroid()
    {
        double cx = 0, cy = 0, a = 0;
        for (int i = 0; i < Polygon.Count; i++)
        {
            var p = Polygon[i];
            var q = Polygon[(i + 1) % Polygon.Count];
            double cross = p.X * q.Y - q.X * p.Y;
            a += cross;
            cx += (p.X + q.X) * cross;
            cy += (p.Y + q.Y) * cross;
        }
        if (Math.Abs(a) < 1e-6) return Polygon.Count > 0 ? Polygon[0] : new Pt(0, 0);
        return new Pt(cx / (3 * a), cy / (3 * a));
    }
}

// Detects closed rooms as bounded faces of the wall graph (planar face traversal).
public static class Rooms
{
    public static List<Room> Detect(WallNodeSequence seq)
    {
        var result = new List<Room>();
        var pos = new Dictionary<int, Pt>();
        foreach (var n in seq.Nodes.Values) pos[n.Id] = new Pt(n.X, n.Y);

        // undirected adjacency (deduped)
        var adj = new Dictionary<int, List<int>>();
        foreach (var id in pos.Keys) adj[id] = new List<int>();
        foreach (var w in seq.Walls)
        {
            int a = w.LeftNode.Id, b = w.RightNode.Id;
            if (a == b || !pos.ContainsKey(a) || !pos.ContainsKey(b)) continue;
            if (!adj[a].Contains(b)) adj[a].Add(b);
            if (!adj[b].Contains(a)) adj[b].Add(a);
        }

        // sort neighbours by angle around each node
        foreach (var id in adj.Keys.ToList())
        {
            var p = pos[id];
            adj[id].Sort((u, v) =>
                Math.Atan2(pos[u].Y - p.Y, pos[u].X - p.X)
                .CompareTo(Math.Atan2(pos[v].Y - p.Y, pos[v].X - p.X)));
        }

        var visited = new HashSet<(int, int)>();
        foreach (var start in adj.Keys)
        {
            foreach (var next in adj[start])
            {
                if (visited.Contains((start, next))) continue;

                var cycle = new List<int>();
                int u = start, v = next;
                int guard = 0;
                while (!visited.Contains((u, v)) && guard++ < 100000)
                {
                    visited.Add((u, v));
                    cycle.Add(u);
                    var neigh = adj[v];
                    int idx = neigh.IndexOf(u);
                    int nextIdx = (idx - 1 + neigh.Count) % neigh.Count;
                    int w = neigh[nextIdx];
                    u = v;
                    v = w;
                }

                if (cycle.Count < 3) continue;
                var poly = cycle.Select(id => pos[id]).ToList();
                double signed = SignedArea(poly);
                // bounded interior faces come out positive with this traversal;
                // the unbounded outer face is negative.
                if (signed > 0.01 * Constants.METER * Constants.METER)
                    result.Add(new Room { Polygon = poly, AreaPx = signed });
            }
        }
        return result;
    }

    public static Room? RoomContaining(WallNodeSequence seq, Pt p)
    {
        Room? best = null;
        foreach (var r in Detect(seq))
            if (PointInPolygon(r.Polygon, p) && (best == null || r.AreaPx < best.AreaPx))
                best = r;
        return best;
    }

    public static double SignedArea(List<Pt> poly)
    {
        double a = 0;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p.X * q.Y - q.X * p.Y;
        }
        return a / 2.0;
    }

    public static bool PointInPolygon(List<Pt> poly, Pt p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if (poly[i].Y > p.Y != poly[j].Y > p.Y &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }
}
