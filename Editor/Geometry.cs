namespace FloorPlan.Editor;

public struct Pt
{
    public double X;
    public double Y;
    public Pt(double x, double y) { X = x; Y = y; }
}

public static class Geometry
{
    public static double EuclideanDistance(double x1, double x2, double y1, double y2)
        => Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));

    // y on the line through a-b for a given x
    public static double GetCorrespondingY(double x, Pt a, Pt b)
        => ((x - a.X) * (b.Y - a.Y)) / (b.X - a.X) + a.Y;

    // snap a value to the nearest multiple of 10
    public static double Snap(double val)
    {
        double rest = val % 10;
        double cat = val - rest;
        if (Math.Abs(rest) < 5) return cat;
        return cat + (rest < 0 ? -10 : 10);
    }
}

// 2x3 affine matrix:  [ a c tx ]
//                     [ b d ty ]
public struct Mat
{
    public double A, B, C, D, Tx, Ty;

    public static Mat Identity => new() { A = 1, B = 0, C = 0, D = 1, Tx = 0, Ty = 0 };

    public static Mat Translate(double x, double y)
        => new() { A = 1, B = 0, C = 0, D = 1, Tx = x, Ty = y };

    public static Mat Scale(double sx, double sy)
        => new() { A = sx, B = 0, C = 0, D = sy, Tx = 0, Ty = 0 };

    public static Mat Rotate(double rad)
    {
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        return new Mat { A = cos, B = sin, C = -sin, D = cos, Tx = 0, Ty = 0 };
    }

    // this * other  (apply 'other' first, then 'this')
    public Mat Mul(Mat o) => new()
    {
        A = A * o.A + C * o.B,
        B = B * o.A + D * o.B,
        C = A * o.C + C * o.D,
        D = B * o.C + D * o.D,
        Tx = A * o.Tx + C * o.Ty + Tx,
        Ty = B * o.Tx + D * o.Ty + Ty
    };

    public Pt Apply(Pt p) => new(A * p.X + C * p.Y + Tx, B * p.X + D * p.Y + Ty);

    public Mat Invert()
    {
        double det = A * D - B * C;
        if (Math.Abs(det) < 1e-12) return Identity;
        double id = 1.0 / det;
        return new Mat
        {
            A = D * id,
            B = -B * id,
            C = -C * id,
            D = A * id,
            Tx = (C * Ty - D * Tx) * id,
            Ty = (B * Tx - A * Ty) * id
        };
    }

    public double[] ToArray() => new[] { A, B, C, D, Tx, Ty };
}
