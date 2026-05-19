namespace FloorPlan.Editor.Model;

public class Wall
{
    public WallNode LeftNode;
    public WallNode RightNode;
    public WallType Type = WallType.Interior;
    public double Thickness = Constants.INTERIOR_WALL_THICKNESS;

    public bool IsExterior => Type == WallType.Exterior;

    public static double ThicknessFor(WallType t) => t switch
    {
        WallType.Exterior => Constants.WALL_THICKNESS,
        WallType.RoomDivider => Constants.ROOM_DIVIDER_THICKNESS,
        _ => Constants.INTERIOR_WALL_THICKNESS
    };

    public double X1, Y1, X2, Y2;
    public double Length;
    public double ThetaRad;
    public double ThetaDeg;

    public Wall(WallNode left, WallNode right)
    {
        LeftNode = left;
        RightNode = right;
        Recompute();
    }

    public void SetType(WallType type)
    {
        Type = type;
        Thickness = ThicknessFor(type);
        LeftNode.Size = Math.Max(LeftNode.Size, Thickness);
        RightNode.Size = Math.Max(RightNode.Size, Thickness);
        Recompute();
    }

    // ordered endpoints (mirrors original setLineCoords)
    private (double, double, double, double) OrderedCoords()
    {
        if (LeftNode.X == RightNode.X)
        {
            return LeftNode.Y < RightNode.Y
                ? (LeftNode.X, LeftNode.Y, RightNode.X, RightNode.Y)
                : (RightNode.X, RightNode.Y, LeftNode.X, LeftNode.Y);
        }
        return LeftNode.X < RightNode.X
            ? (LeftNode.X, LeftNode.Y, RightNode.X, RightNode.Y)
            : (RightNode.X, RightNode.Y, LeftNode.X, LeftNode.Y);
    }

    public void Recompute()
    {
        (X1, Y1, X2, Y2) = OrderedCoords();
        double theta = Math.Atan2(Y2 - Y1, X2 - X1);
        ThetaRad = theta;
        double deg = theta * 180.0 / Math.PI;
        if (deg < 0) deg = 360 + deg;
        ThetaDeg = deg;
        Length = Geometry.EuclideanDistance(X1, X2, Y1, Y2);
        LeftNode.AngleRad = theta;
        RightNode.AngleRad = theta;
    }

    // world-space transform of the wall band; local rect is (0,0,Length,Thickness)
    public Mat LocalMatrix()
        => Mat.Translate(X1, Y1).Mul(Mat.Rotate(ThetaRad)).Mul(Mat.Translate(0, -Thickness / 2));

    public bool HitTest(Pt world)
    {
        Pt l = LocalMatrix().Invert().Apply(world);
        return l.X >= 0 && l.X <= Length && l.Y >= 0 && l.Y <= Thickness;
    }
}
