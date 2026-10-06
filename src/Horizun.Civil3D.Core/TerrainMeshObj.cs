using System.Globalization;
using System.Text;

namespace Horizun.Civil3D.Core;

/// <summary>Exact visible TIN connectivity, in metres and drawing axes. No
/// reprojection, welding, retriangulation, face filling or coordinate offset.</summary>
public static class TerrainMeshObj
{
    public static byte[] Write(LxSurface surface, double metresPerUnit)
    {
        if (!Hz.IsFinite(metresPerUnit) || metresPerUnit <= 0)
            throw new ArgumentException("A finite positive conversion factor is required.");
        var text = new StringBuilder("# Horizun Civil 3D visible TIN\n# units: meter; axes: east north elevation; no transform\n");
        foreach (var p in surface.Points)
        {
            var x = p.X * metresPerUnit; var y = p.Y * metresPerUnit; var z = p.Z * metresPerUnit;
            if (!Hz.IsFinite(x) || !Hz.IsFinite(y) || !Hz.IsFinite(z))
                throw new ArgumentException("Converted OBJ coordinates must be finite.");
            text.Append("v ").Append(x.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(y.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
                .Append(z.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }
        foreach (var (a, b, c) in surface.Faces)
        {
            if (a < 0 || b < 0 || c < 0 || a >= surface.Points.Count || b >= surface.Points.Count || c >= surface.Points.Count)
                throw new ArgumentException("OBJ face indices are outside the vertex array.");
            text.Append("f ").Append((a + 1).ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append((b + 1).ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append((c + 1).ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        if (bytes.Length > RevitTerrainPackage.MaxPayloadBytes)
            throw new ArgumentException("OBJ exceeds the 64 MiB asset guard.");
        return bytes;
    }
}
