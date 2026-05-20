namespace FloorPlan.Editor.Model;

public class Furniture
{
    public int Id;
    public string Key;
    public string Name;
    public string Fill;
    public string Symbol;
    public string ImagePath = "";

    public double Width;   // pixels
    public double Height;  // pixels

    // free furniture: (X,Y) is the centre in world coords.
    // attached furniture: (X,Y) is local to the parent wall band.
    public double X;
    public double Y;
    public double Rotation;       // radians (free furniture only)
    public double ScaleX = 1;
    public double ScaleY = 1;
    public int Orientation;
    public int ZIndex = 1;

    public bool IsAttached;
    public Wall? AttachedTo;
    public int AttachedToLeft;
    public int AttachedToRight;

    public Furniture(CatalogItem item, int id)
    {
        Id = id;
        Key = item.Key;
        Name = item.Name;
        Fill = item.Fill;
        Symbol = item.Symbol;
        ImagePath = item.ImagePath;
        Width = item.Width * Constants.METER;
        Height = item.Height * Constants.METER;
    }

    public bool HasImage => !string.IsNullOrEmpty(EffectiveImagePath);

    // attached doors use a different SVG depending on the wall's type
    public string EffectiveImagePath
    {
        get
        {
            if (IsDoor && IsAttached && AttachedTo != null)
                return AttachedTo.Type == WallType.Exterior
                    ? "furniture/door-exterior.svg"
                    : "furniture/door-interior.svg";
            return ImagePath;
        }
    }

    public bool IsDoor => Key == "door";
    public bool IsWindow => Key == "window";
    public bool IsCompass => Key == "compass";
    public bool IsStairs => Key.StartsWith("stair_");
    public StairKind Stair => Key switch
    {
        "stair_straight" => StairKind.Straight,
        "stair_l" => StairKind.LShaped,
        "stair_spiral" => StairKind.Spiral,
        _ => StairKind.None
    };

    // local rect is always (0,0,Width,Height); this matrix maps it to world space
    public Mat LocalMatrix()
    {
        if (IsAttached && AttachedTo != null)
        {
            // x is centre-pivot so an orientation flip doesn't shift the door along the wall.
            // for doors, y is edge-pivot at the wall face so flipping ScaleY puts the door
            // on the other side of the wall (always opening outward).
            // for windows, y is centre-pivot so stretching grows symmetrically about the centre.
            double ya = IsDoor ? 0 : Height / 2;
            return AttachedTo.LocalMatrix()
                .Mul(Mat.Translate(X + Width / 2, Y + ya))
                .Mul(Mat.Scale(ScaleX, ScaleY))
                .Mul(Mat.Translate(-Width / 2, -ya));
        }
        return Mat.Translate(X, Y)
            .Mul(Mat.Rotate(Rotation))
            .Mul(Mat.Scale(ScaleX, ScaleY))
            .Mul(Mat.Translate(-Width / 2, -Height / 2));
    }

    public bool HitTest(Pt world)
    {
        Pt l = LocalMatrix().Invert().Apply(world);
        return l.X >= 0 && l.X <= Width && l.Y >= 0 && l.Y <= Height;
    }

    public void CycleOrientation() => CycleOrientationTo((Orientation + 1) % 4);

    public void CycleOrientationTo(int orientation)
    {
        Orientation = ((orientation % 4) + 4) % 4;
        double mag = Math.Abs(ScaleX) == 0 ? 1 : Math.Abs(ScaleX);
        double magY = Math.Abs(ScaleY) == 0 ? 1 : Math.Abs(ScaleY);
        ScaleX = (Orientation == 1 || Orientation == 2) ? -mag : mag;
        ScaleY = (Orientation == 2 || Orientation == 3) ? -magY : magY;
    }
}
