namespace FloorPlan.Editor.Model;

public class WallNode
{
    public int Id { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Size { get; set; } = Constants.INTERIOR_WALL_THICKNESS;
    public double AngleRad { get; set; }

    public WallNode(double x, double y, int id)
    {
        X = x;
        Y = y;
        Id = id;
    }
}
