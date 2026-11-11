namespace CivilAI;

internal readonly record struct TrianglePoint(double X, double Y, double Z);
internal static class TriangleSlope
{
    public static bool TryCalculate(TrianglePoint a, TrianglePoint b, TrianglePoint c, out double percent, out TrianglePoint centre)
    {
        percent = double.NaN;
        centre = new(a.X / 3 + b.X / 3 + c.X / 3, a.Y / 3 + b.Y / 3 + c.Y / 3, a.Z / 3 + b.Z / 3 + c.Z / 3);
        var ux = b.X - a.X; var uy = b.Y - a.Y; var uz = b.Z - a.Z;
        var vx = c.X - a.X; var vy = c.Y - a.Y; var vz = c.Z - a.Z;
        var scale = new[] { Math.Abs(ux), Math.Abs(uy), Math.Abs(uz), Math.Abs(vx), Math.Abs(vy), Math.Abs(vz) }.Max();
        if (!double.IsFinite(scale) || scale == 0) return false;
        ux /= scale; uy /= scale; uz /= scale; vx /= scale; vy /= scale; vz /= scale;
        var nx = uy * vz - uz * vy; var ny = uz * vx - ux * vz; var nz = ux * vy - uy * vx;
        if (Math.Abs(nz) <= 1e-14) return false; // Numerically degenerate/near-vertical projected triangle.
        percent = Math.Sqrt(nx * nx + ny * ny) / Math.Abs(nz) * 100;
        return double.IsFinite(percent) && double.IsFinite(centre.X) && double.IsFinite(centre.Y) && double.IsFinite(centre.Z);
    }
}
