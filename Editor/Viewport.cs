namespace FloorPlan.Editor;

public class Viewport
{
    public double CornerX;
    public double CornerY;
    public double Scale = 1.0;
    public double ScreenW = 800;
    public double ScreenH = 600;

    public Viewport()
    {
        // start centred on the world
        CornerX = Constants.WORLD_SIZE / 2 - ScreenW / 2;
        CornerY = Constants.WORLD_SIZE / 2 - ScreenH / 2;
    }

    public Mat BaseMatrix => new()
    {
        A = Scale,
        B = 0,
        C = 0,
        D = Scale,
        Tx = -CornerX * Scale,
        Ty = -CornerY * Scale
    };

    public Pt ScreenToWorld(double sx, double sy)
        => new(sx / Scale + CornerX, sy / Scale + CornerY);

    public Pt WorldToScreen(double wx, double wy)
        => new((wx - CornerX) * Scale, (wy - CornerY) * Scale);

    public void Resize(double w, double h)
    {
        ScreenW = w;
        ScreenH = h;
        Clamp();
    }

    public void PanBy(double dxScreen, double dyScreen)
    {
        CornerX -= dxScreen / Scale;
        CornerY -= dyScreen / Scale;
        Clamp();
    }

    public void ZoomAt(double sx, double sy, double factor)
    {
        Pt before = ScreenToWorld(sx, sy);
        Scale = Math.Clamp(Scale * factor, Constants.MIN_SCALE, Constants.MAX_SCALE);
        Pt after = ScreenToWorld(sx, sy);
        CornerX += before.X - after.X;
        CornerY += before.Y - after.Y;
        Clamp();
    }

    private void Clamp()
    {
        double viewW = ScreenW / Scale;
        double viewH = ScreenH / Scale;
        // when zoomed out past the world size, centre the world instead of pinning
        CornerX = viewW >= Constants.WORLD_SIZE
            ? (Constants.WORLD_SIZE - viewW) / 2
            : Math.Clamp(CornerX, 0, Constants.WORLD_SIZE - viewW);
        CornerY = viewH >= Constants.WORLD_SIZE
            ? (Constants.WORLD_SIZE - viewH) / 2
            : Math.Clamp(CornerY, 0, Constants.WORLD_SIZE - viewH);
    }
}
