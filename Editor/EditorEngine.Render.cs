using FloorPlan.Editor.Model;

namespace FloorPlan.Editor;

public partial class EditorEngine
{
    private static string Meters(double px) => (Math.Round(Math.Abs(px) / Constants.METER * 100) / 100).ToString("0.00") + "m";

    private static string WallFill(WallType t) => t switch
    {
        WallType.Exterior => "#000000",
        WallType.RoomDivider => "#8a9099",
        _ => "#000000"
    };

    private const string StairLine = "#5a6b7b";
    private const string StairArrow = "#1565c0";

    private void DrawCompass(Scene s, Furniture f, Mat m)
    {
        double W = f.Width, H = f.Height;
        double cx = W / 2, cy = H / 2;
        double R = Math.Min(W, H) / 2 * 0.94;
        Pt P(double x, double y) => m.Apply(new Pt(x, y));

        // dial
        var ring = new List<Pt>();
        for (int i = 0; i < 48; i++)
        {
            double a = i / 48.0 * Math.PI * 2;
            ring.Add(P(cx + R * Math.Cos(a), cy + R * Math.Sin(a)));
        }
        s.PolygonScreen(ring, "#ffffff", "#33373d", 2);

        // N points to local -Y (top of the piece); rotates with the furniture
        Pt n = P(cx, cy - R * 0.86);
        Pt sP = P(cx, cy + R * 0.86);
        Pt e = P(cx + R * 0.86, cy);
        Pt w = P(cx - R * 0.86, cy);
        Pt cn = P(cx, cy);

        // needle: red north half, grey south half
        Pt bl = P(cx - R * 0.16, cy);
        Pt br = P(cx + R * 0.16, cy);
        s.PolygonScreen(new List<Pt> { n, br, bl }, "#c62828", "#c62828", 1);
        s.PolygonScreen(new List<Pt> { sP, br, bl }, "#7a7f87", "#7a7f87", 1);

        // hub
        var hub = new List<Pt>();
        for (int i = 0; i < 16; i++)
        {
            double a = i / 16.0 * Math.PI * 2;
            hub.Add(P(cx + R * 0.08 * Math.Cos(a), cy + R * 0.08 * Math.Sin(a)));
        }
        s.PolygonScreen(hub, "#33373d", "#33373d", 1);

        double fs = Math.Clamp(R * Vp.Scale * 0.5, 9, 20);
        Pt nl = P(cx, cy - R * 1.18);
        s.TextScreen(nl.X, nl.Y, "N", "#c62828", fs);
        s.TextScreen(P(cx, cy + R * 1.18).X, P(cx, cy + R * 1.18).Y, "S", "#33373d", fs);
        s.TextScreen(P(cx + R * 1.18, cy).X, P(cx + R * 1.18, cy).Y, "E", "#33373d", fs);
        s.TextScreen(P(cx - R * 1.18, cy).X, P(cx - R * 1.18, cy).Y, "W", "#33373d", fs);
    }

    private void DrawStairs(Scene s, Furniture f, Mat m)
    {
        double W = f.Width, H = f.Height;
        Pt S(double x, double y) => m.Apply(new Pt(x, y));
        void Seg(double x1, double y1, double x2, double y2, string col, double lw)
        {
            Pt a = S(x1, y1), b = S(x2, y2);
            s.LineScreen(a.X, a.Y, b.X, b.Y, col, lw);
        }
        void Arrow(double x1, double y1, double x2, double y2)
        {
            Seg(x1, y1, x2, y2, StairArrow, 2);
            double ang = Math.Atan2(y2 - y1, x2 - x1);
            double hl = Math.Min(W, H) * 0.12;
            Seg(x2, y2, x2 - hl * Math.Cos(ang - 0.4), y2 - hl * Math.Sin(ang - 0.4), StairArrow, 2);
            Seg(x2, y2, x2 - hl * Math.Cos(ang + 0.4), y2 - hl * Math.Sin(ang + 0.4), StairArrow, 2);
        }

        switch (f.Stair)
        {
            case StairKind.Straight:
            {
                int steps = Math.Clamp((int)Math.Round(H / 26.0), 4, 30);
                double dy = H / steps;
                for (int i = 1; i < steps; i++)
                    Seg(0, i * dy, W, i * dy, StairLine, 1);
                Arrow(W / 2, H * 0.9, W / 2, H * 0.12);
                Pt up = S(W / 2, H * 0.97);
                s.TextScreen(up.X, up.Y, "UP", StairArrow, 11);
                break;
            }
            case StairKind.LShaped:
            {
                double leg = Math.Min(W, H) * 0.5;
                // vertical leg (left), treads horizontal
                int va = Math.Clamp((int)Math.Round((H - leg) / 26.0), 3, 24);
                double dva = (H - leg) / va;
                for (int i = 1; i < va; i++)
                    Seg(0, i * dva, leg, i * dva, StairLine, 1);
                // horizontal leg (bottom), treads vertical
                int hb = Math.Clamp((int)Math.Round((W - leg) / 26.0), 3, 24);
                double dhb = (W - leg) / hb;
                for (int i = 1; i < hb; i++)
                    Seg(leg + i * dhb, H - leg, leg + i * dhb, H, StairLine, 1);
                // landing outline
                Seg(0, H - leg, W, H - leg, StairLine, 1);
                Seg(leg, 0, leg, H, StairLine, 1);
                // path: from end of bottom leg -> landing -> up the left leg
                Arrow(W * 0.95, H - leg / 2, leg / 2, H - leg / 2);
                Arrow(leg / 2, H - leg, leg / 2, H * 0.1);
                Pt up = S(W * 0.93, H - leg / 2 + Math.Min(W, H) * 0.14);
                s.TextScreen(up.X, up.Y, "UP", StairArrow, 11);
                break;
            }
            case StairKind.Spiral:
            {
                double cx = W / 2, cy = H / 2;
                double R = Math.Min(W, H) / 2 * 0.96;
                double r0 = R * 0.16;
                // outer + inner circles as transformed polygons
                var outer = new List<Pt>();
                var inner = new List<Pt>();
                for (int i = 0; i < 40; i++)
                {
                    double a = i / 40.0 * Math.PI * 2;
                    outer.Add(S(cx + R * Math.Cos(a), cy + R * Math.Sin(a)));
                    inner.Add(S(cx + r0 * Math.Cos(a), cy + r0 * Math.Sin(a)));
                }
                s.PolygonScreen(outer, null, StairLine, 1);
                s.PolygonScreen(inner, "#cfd6dd", StairLine, 1);
                int spokes = 12;
                for (int i = 0; i < spokes; i++)
                {
                    double a = i / (double)spokes * Math.PI * 2;
                    Seg(cx + r0 * Math.Cos(a), cy + r0 * Math.Sin(a),
                        cx + R * Math.Cos(a), cy + R * Math.Sin(a), StairLine, 1);
                }
                // direction arrow along an arc
                double a0 = -Math.PI / 2;
                Arrow(cx + (R * 0.7) * Math.Cos(a0), cy + (R * 0.7) * Math.Sin(a0),
                      cx + (R * 0.7) * Math.Cos(a0 + 0.9), cy + (R * 0.7) * Math.Sin(a0 + 0.9));
                Pt up = S(cx, cy);
                s.TextScreen(up.X, up.Y, "UP", StairArrow, 11);
                break;
            }
        }
    }

    public Scene BuildScene()
    {
        var s = new Scene();
        s.Clear("#ebebeb");
        DrawGrid(s);

        var seq = Plan.Current.Seq;

        // detect rooms once if needed
        List<Room>? rooms = null;
        if (ShowRoomArea || Plan.Current.RoomLabels.Count > 0)
            rooms = Rooms.Detect(seq);

        // room area shading (under the walls)
        if (ShowRoomArea && rooms != null)
        {
            foreach (var r in rooms)
            {
                var scr = r.Polygon.Select(p => Vp.WorldToScreen(p.X, p.Y)).ToList();
                s.PolygonScreen(scr, "rgba(21,101,192,0.10)", "rgba(21,101,192,0.35)", 1);
            }
        }

        // walls
        foreach (var w in seq.Walls)
        {
            Mat m = Vp.BaseMatrix.Mul(w.LocalMatrix());
            s.Rect(m, w.Length, w.Thickness, WallFill(w.Type), "#0d0e10", 1);
        }

        // wall nodes
        foreach (var n in seq.Nodes.Values)
        {
            Mat m = Vp.BaseMatrix
                .Mul(Mat.Translate(n.X, n.Y))
                .Mul(Mat.Rotate(n.AngleRad))
                .Mul(Mat.Translate(-n.Size / 2, -n.Size / 2));
            s.Rect(m, n.Size, n.Size, "#222222");
        }

        // furniture (z order)
        foreach (var f in Plan.Current.FurnitureMap.Values.OrderBy(f => f.ZIndex))
        {
            Mat m = Vp.BaseMatrix.Mul(f.LocalMatrix());
            string fill = f.Fill;

            if (f.HasImage)
            {
                s.Image(m, f.Width, f.Height, f.ImagePath);
            }
            else if (f.IsCompass)
            {
                DrawCompass(s, f, m);
            }
            else
            {
                s.Rect(m, f.Width, f.Height, fill, "#2b2b2b", 1.5);
                Pt c = m.Apply(new Pt(f.Width / 2, f.Height / 2));
                double labelPx = Math.Min(18, Math.Max(9, 10 * Vp.Scale * 0.6));
                s.TextScreen(c.X, c.Y, f.Symbol, "#1a1a1a", labelPx);
            }
        }

        // wall length labels
        if (Plan.LabelsVisible)
        {
            foreach (var w in seq.Walls)
            {
                Pt mid = Vp.WorldToScreen((w.X1 + w.X2) / 2, (w.Y1 + w.Y2) / 2);
                s.TextScreen(mid.X, mid.Y - 14 - w.Thickness * Vp.Scale / 2,
                    Meters(w.Length - Constants.WALL_THICKNESS), "#0d3b66", 13);
            }
        }

        // room labels
        foreach (var rl in Plan.Current.RoomLabels)
        {
            Pt at = Vp.WorldToScreen(rl.X, rl.Y);
            s.TextScreen(at.X, at.Y, rl.Name, "#243b53", 15);
            if (ShowRoomArea && rooms != null)
            {
                Room? room = null;
                foreach (var r in rooms)
                    if (Rooms.PointInPolygon(r.Polygon, new Pt(rl.X, rl.Y)) &&
                        (room == null || r.AreaPx < room.AreaPx))
                        room = r;
                if (room != null)
                    s.TextScreen(at.X, at.Y + 18, room.AreaM2.ToString("0.0") + " m²", "#5a6b7b", 12);
            }
        }

        // hover highlight (shows what the Delete key will remove)
        if (_drag == DragMode.None && Selected == null &&
            ActiveTool is Tool.Edit or Tool.Remove or Tool.View)
        {
            const string hi = "#e53935";
            if (_hoverFur != null &&
                Plan.Current.FurnitureMap.TryGetValue(_hoverFur.Id, out var hv) && hv == _hoverFur)
            {
                Mat hm = Vp.BaseMatrix.Mul(_hoverFur.LocalMatrix());
                s.Rect(hm, _hoverFur.Width, _hoverFur.Height, null, hi, 2.5);
            }
            else if (_hoverNode != null && Plan.Current.Seq.Nodes.ContainsKey(_hoverNode.Id))
            {
                Pt p = Vp.WorldToScreen(_hoverNode.X, _hoverNode.Y);
                s.CircleScreen(p.X, p.Y, _hoverNode.Size * Vp.Scale / 2 + 4, null, hi, 2.5);
            }
            else if (_hoverWall != null && Plan.Current.Seq.Walls.Contains(_hoverWall))
            {
                Mat hm = Vp.BaseMatrix.Mul(_hoverWall.LocalMatrix());
                s.Rect(hm, _hoverWall.Length, _hoverWall.Thickness, null, hi, 2.5);
            }
        }

        // selection + handles
        DrawSelection(s);

        // wall-add preview (reflects magnet / ortho snapping)
        if (ActiveTool == Tool.WallAdd)
        {
            var tgt = ResolveWallTarget(_mouseWorld);
            Pt bw = tgt.Point;
            if (_wallPreviewStart.HasValue)
            {
                Pt a = Vp.WorldToScreen(_wallPreviewStart.Value.X, _wallPreviewStart.Value.Y);
                Pt b = Vp.WorldToScreen(bw.X, bw.Y);
                s.LineScreen(a.X, a.Y, b.X, b.Y, "#1f1f1f", 2);
                double len = Geometry.EuclideanDistance(_wallPreviewStart.Value.X, bw.X,
                                                        _wallPreviewStart.Value.Y, bw.Y);
                s.TextScreen((a.X + b.X) / 2, (a.Y + b.Y) / 2 - 10, Meters(len), "#1f1f1f", 13);
            }
            // magnet highlight on the node we'd snap to
            if (tgt.Node != null)
            {
                Pt np = Vp.WorldToScreen(tgt.Node.X, tgt.Node.Y);
                s.CircleScreen(np.X, np.Y, 9, null, "#1565c0", 2);
            }
        }

        // measure preview
        if (_measureStart.HasValue)
        {
            Pt a = Vp.WorldToScreen(_measureStart.Value.X, _measureStart.Value.Y);
            Pt b = Vp.WorldToScreen(_measureCur.X, _measureCur.Y);
            s.LineScreen(a.X, a.Y, b.X, b.Y, "#c62828", 2);
            double len = Geometry.EuclideanDistance(_measureStart.Value.X, _measureCur.X,
                                                    _measureStart.Value.Y, _measureCur.Y);
            s.TextScreen((a.X + b.X) / 2, (a.Y + b.Y) / 2 - 10, Meters(len), "#c62828", 13);
        }

        // pointer indicator
        if (ActiveTool == Tool.WallAdd)
        {
            Pt rp = ResolveWallTarget(_mouseWorld).Point;
            Pt p = Vp.WorldToScreen(rp.X, rp.Y);
            s.CircleScreen(p.X, p.Y, 3, "#000000");
        }
        else if (ActiveTool == Tool.Measure)
        {
            Pt p = Vp.WorldToScreen(SnapWorld(_mouseWorld).X, SnapWorld(_mouseWorld).Y);
            s.CircleScreen(p.X, p.Y, 3, "#000000");
        }

        return s;
    }

    private void DrawGrid(Scene s)
    {
        double step = Constants.METER;
        double W = Constants.WORLD_SIZE;
        double left = Vp.CornerX;
        double top = Vp.CornerY;
        double right = Vp.CornerX + Vp.ScreenW / Vp.Scale;
        double bottom = Vp.CornerY + Vp.ScreenH / Vp.Scale;

        // grid is confined to the world rectangle [0,W] on both axes so it
        // stays a proper grid (not just verticals) when zoomed out / centered
        double x0 = Math.Max(0, Math.Floor(left / step) * step);
        double x1 = Math.Min(W, right);
        for (double x = x0; x <= x1 + 0.5; x += step)
        {
            Pt a = Vp.WorldToScreen(x, 0);
            Pt b = Vp.WorldToScreen(x, W);
            bool major = Math.Abs(x % (5 * step)) < 0.5;
            s.LineScreen(a.X, a.Y, b.X, b.Y, major ? "#9aa6b5" : "#c6ccd6", major ? 1.5 : 1);
        }
        double y0 = Math.Max(0, Math.Floor(top / step) * step);
        double y1 = Math.Min(W, bottom);
        for (double y = y0; y <= y1 + 0.5; y += step)
        {
            Pt a = Vp.WorldToScreen(0, y);
            Pt b = Vp.WorldToScreen(W, y);
            bool major = Math.Abs(y % (5 * step)) < 0.5;
            s.LineScreen(a.X, a.Y, b.X, b.Y, major ? "#9aa6b5" : "#c6ccd6", major ? 1.5 : 1);
        }
    }

    private void DrawSelection(Scene s)
    {
        if (Selected == null || ActiveTool != Tool.Edit) return;
        var f = Selected;
        Mat m = Vp.BaseMatrix.Mul(f.LocalMatrix());
        Pt p0 = m.Apply(new Pt(0, 0));
        Pt p1 = m.Apply(new Pt(f.Width, 0));
        Pt p2 = m.Apply(new Pt(f.Width, f.Height));
        Pt p3 = m.Apply(new Pt(0, f.Height));
        s.LineScreen(p0.X, p0.Y, p1.X, p1.Y, "#1565c0", 2);
        s.LineScreen(p1.X, p1.Y, p2.X, p2.Y, "#1565c0", 2);
        s.LineScreen(p2.X, p2.Y, p3.X, p3.Y, "#1565c0", 2);
        s.LineScreen(p3.X, p3.Y, p0.X, p0.Y, "#1565c0", 2);

        // connector stub from the top-right corner to the rotate grip
        foreach (var (type, x, y) in HandlePositions())
            if (type == HandleType.Rotate)
                s.LineScreen(p1.X, p1.Y, x, y, "#1565c0", 1.5);

        foreach (var (type, x, y) in HandlePositions())
        {
            if (type == HandleType.Rotate)
            {
                s.CircleScreen(x, y, 9, "#1565c0", "#0d3b66", 1.5);
                s.TextScreen(x, y, "⟳", "#ffffff", 13);
            }
            else
            {
                s.RectScreen(x - 6, y - 6, 12, 12, "#ffffff", "#1565c0", 2);
            }
        }
    }
}
