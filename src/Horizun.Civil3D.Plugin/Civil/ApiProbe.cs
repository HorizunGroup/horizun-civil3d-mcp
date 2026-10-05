// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - in-process API probe.
//
// Golden rule of this project: never code against a remembered Civil 3D API.
// Method names and overloads change between years and the documentation has
// gaps. The probe dumps the REAL public surface of the assemblies loaded in
// this acad.exe, and every dump is kept (probes\<year>\, and the repo's
// docs/api-probes/<year>/) so year-to-year differences are versioned.
// -----------------------------------------------------------------------------
using System.Reflection;
using System.Text;

namespace Horizun.Civil3D.Plugin.Civil;

internal static class ApiProbe
{
    public static readonly string[] DefaultTypes =
    {
        "Autodesk.Civil.ApplicationServices.CivilDocument",
        "Autodesk.Civil.DatabaseServices.Entity",
        "Autodesk.Civil.DatabaseServices.Surface",
        "Autodesk.Civil.DatabaseServices.TinSurface",
        "Autodesk.Civil.DatabaseServices.TinVolumeSurface",
        "Autodesk.Civil.DatabaseServices.SurfaceAnalysis",
        "Autodesk.Civil.DatabaseServices.SurfaceDefinitionBreaklines",
        "Autodesk.Civil.DatabaseServices.SurfaceDefinitionBoundaries",
        "Autodesk.Civil.DatabaseServices.Alignment",
        "Autodesk.Civil.DatabaseServices.Profile",
        "Autodesk.Civil.DatabaseServices.Corridor",
        "Autodesk.Civil.DatabaseServices.FeatureLine",
        "Autodesk.Civil.DatabaseServices.Site",
        "Autodesk.Civil.DatabaseServices.Grading",
        "Autodesk.Civil.DatabaseServices.Styles.StylesRoot",
        "Autodesk.Civil.DatabaseServices.Styles.SurfaceStyle",
    };

    public static Assembly? FindAssembly(string simpleName) =>
        AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

    public static string Dump(Assembly asm, IEnumerable<string> typeNames)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# " + asm.GetName().Name + " " + asm.GetName().Version + " (live, in-process)");
        foreach (var tn in typeNames)
        {
            var t = asm.GetType(tn, false);
            if (t == null) { sb.AppendLine("NOTFOUND " + tn); continue; }
            string baseName;
            try { baseName = t.BaseType?.FullName ?? ""; } catch (Exception e) { baseName = "? " + e.GetType().Name; }
            sb.AppendLine("== " + t.FullName + " : " + baseName);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var m in t.GetMembers(flags).OrderBy(m => m.MemberType).ThenBy(m => m.Name, StringComparer.Ordinal))
            {
                try
                {
                    switch (m)
                    {
                        case MethodInfo mi when !mi.IsSpecialName:
                            sb.AppendLine($"  M {(mi.IsStatic ? "static " : "")}{Name(mi.ReturnType)} {mi.Name}({Params(mi.GetParameters())})");
                            break;
                        case PropertyInfo pi:
                            var set = pi.SetMethod is { IsPublic: true };
                            var idx = pi.GetIndexParameters();
                            sb.AppendLine($"  P {Name(pi.PropertyType)} {pi.Name}{(idx.Length > 0 ? "[" + Params(idx) + "]" : "")} {{get;{(set ? "set;" : "")}}}");
                            break;
                        case ConstructorInfo ci:
                            sb.AppendLine($"  C ({Params(ci.GetParameters())})");
                            break;
                        case FieldInfo fi when t.IsEnum && fi.IsStatic:
                            sb.AppendLine("  E " + fi.Name);
                            break;
                        case EventInfo ei:
                            sb.AppendLine("  V " + ei.Name);
                            break;
                    }
                }
                catch (Exception e) { sb.AppendLine("  ? " + m.Name + " " + e.Message); }
            }
        }
        return sb.ToString();
    }

    public static string Find(Assembly asm, string fragment)
    {
        Type[] types;
        try { types = asm.GetExportedTypes(); }
        catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
        var sb = new StringBuilder();
        sb.AppendLine("# " + asm.GetName().Name + " " + asm.GetName().Version + " types matching '" + fragment + "'");
        foreach (var t in types.Where(t => t.FullName!.Contains(fragment, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.FullName))
            sb.AppendLine(t.FullName);
        return sb.ToString();
    }

    private static string Params(ParameterInfo[] ps) => string.Join(", ", ps.Select(p => Name(p.ParameterType) + " " + p.Name));

    private static string Name(Type t) => t.IsGenericType
        ? t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">"
        : t.Name;
}
