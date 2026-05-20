namespace FloorPlan.Editor;

// A single draw primitive. Coordinates are in screen space unless a matrix
// (M) is supplied, in which case the JS renderer applies it and the
// primitive is drawn in its local space.
public class DrawCmd
{
    public string T { get; set; } = "";
    public double[]? M { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double R { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string? Fill { get; set; }
    public string? Stroke { get; set; }
    public double Lw { get; set; }
    public string? S { get; set; }
    public double Fs { get; set; }
    public string? Al { get; set; }
    public double[]? Pts { get; set; }
    public double[]? Dash { get; set; }
}

public class Scene
{
    public List<DrawCmd> Cmds { get; } = new();

    public void Rect(Mat m, double w, double h, string? fill, string? stroke = null, double lw = 1, double[]? dash = null)
        => Cmds.Add(new DrawCmd { T = "rect", M = m.ToArray(), W = w, H = h, Fill = fill, Stroke = stroke, Lw = lw, Dash = dash });

    public void Image(Mat m, double w, double h, string url)
        => Cmds.Add(new DrawCmd { T = "img", M = m.ToArray(), W = w, H = h, S = url });

    public void RectScreen(double x, double y, double w, double h, string? fill, string? stroke = null, double lw = 1)
        => Cmds.Add(new DrawCmd { T = "rect", X = x, Y = y, W = w, H = h, Fill = fill, Stroke = stroke, Lw = lw });

    public void LineScreen(double x1, double y1, double x2, double y2, string stroke, double lw = 1, double[]? dash = null)
        => Cmds.Add(new DrawCmd { T = "line", X = x1, Y = y1, X2 = x2, Y2 = y2, Stroke = stroke, Lw = lw, Dash = dash });

    public void CircleScreen(double x, double y, double r, string? fill, string? stroke = null, double lw = 1)
        => Cmds.Add(new DrawCmd { T = "circle", X = x, Y = y, W = r, Fill = fill, Stroke = stroke, Lw = lw });

    public void TextScreen(double x, double y, string text, string color, double size, string align = "center")
        => Cmds.Add(new DrawCmd { T = "text", X = x, Y = y, S = text, Fill = color, Fs = size, Al = align });

    public void PolygonScreen(IReadOnlyList<Pt> pts, string? fill, string? stroke = null, double lw = 1)
    {
        var arr = new double[pts.Count * 2];
        for (int i = 0; i < pts.Count; i++) { arr[i * 2] = pts[i].X; arr[i * 2 + 1] = pts[i].Y; }
        Cmds.Add(new DrawCmd { T = "poly", Pts = arr, Fill = fill, Stroke = stroke, Lw = lw });
    }

    public void Clear(string color)
        => Cmds.Add(new DrawCmd { T = "clear", Fill = color });
}
