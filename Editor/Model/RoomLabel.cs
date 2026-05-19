namespace FloorPlan.Editor.Model;

public class RoomLabel
{
    public int Id;
    public double X;
    public double Y;
    public string Name;

    public RoomLabel(int id, double x, double y, string name)
    {
        Id = id;
        X = x;
        Y = y;
        Name = name;
    }
}
