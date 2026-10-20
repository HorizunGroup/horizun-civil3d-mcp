// Horizun Civil 3D API probe (offline).
// Reads the public surface of the INSTALLED Civil 3D / AutoCAD managed DLLs through
// MetadataLoadContext, without starting Civil 3D. Rule: every API the plug-in calls
// is first confirmed here and the dump is versioned under docs/api-probes/<year>/.
//
// Usage:
//   hz-c3d-apiprobe --year 2025 [--asm AeccDbMgd.dll] TYPE [TYPE...]
//   hz-c3d-apiprobe --year 2025 [--asm AeccDbMgd.dll] --find NAMEPART
using System.Reflection;
using System.Runtime.InteropServices;

var year = "2025";
var asmName = "AeccDbMgd.dll";
string? acadRoot = null;
string? runtimeRoot = null;
string? find = null;
var types = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--year": year = args[++i]; break;
        case "--asm": asmName = args[++i]; break;
        case "--acad-dir": acadRoot = args[++i]; break;
        case "--runtime-dir": runtimeRoot = args[++i]; break;
        case "--find": find = args[++i]; break;
        default: types.Add(args[i]); break;
    }
}

var acad = acadRoot ?? $@"C:\Program Files\Autodesk\AutoCAD {year}";
var c3d = Path.Combine(acad, "C3D");
if (!File.Exists(Path.Combine(c3d, "AeccDbMgd.dll")))
{
    Console.Error.WriteLine($"Civil 3D {year} not found (no AeccDbMgd.dll under {c3d}).");
    return 2;
}

var paths = new List<string>();
// Match metadata references to the real host framework, including Framework's mscorlib.
var host = Horizun.Civil3D.Server.HostInspection.Inspect(acad, int.Parse(year));
var hostRuntime = host["runtime"]!.GetValue<string>();
var sharedRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.FullName;
string LatestRuntime(string name, int major) => Directory.GetDirectories(Path.Combine(sharedRoot, name))
    .Select(p => (Path: p, Version: Version.TryParse(Path.GetFileName(p), out var v) ? v : null))
    .Where(p => p.Version?.Major == major).OrderByDescending(p => p.Version).First().Path;
runtimeRoot ??= hostRuntime == "net48"
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319")
    : LatestRuntime("Microsoft.NETCore.App", hostRuntime == "net10" ? 10 : 8);
paths.AddRange(Directory.GetFiles(acad, "*.dll"));
paths.AddRange(Directory.GetFiles(c3d, "*.dll"));
var aca = Path.Combine(acad, "ACA");
if (Directory.Exists(aca)) paths.AddRange(Directory.GetFiles(aca, "*.dll"));
paths.AddRange(Directory.GetFiles(runtimeRoot, "*.dll"));
var desktopRoot = hostRuntime == "net48" ? Path.Combine(runtimeRoot, "WPF")
    : LatestRuntime("Microsoft.WindowsDesktop.App", hostRuntime == "net10" ? 10 : 8);
if (Directory.Exists(desktopRoot)) paths.AddRange(Directory.GetFiles(desktopRoot, "*.dll"));
var unique = paths.GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Select(g => g.First());
using var mlc = new MetadataLoadContext(new PathAssemblyResolver(unique), hostRuntime == "net48" ? "mscorlib" : "System.Private.CoreLib");

var asmPath = new[] { c3d, Path.Combine(acad, "ACA"), acad }.Select(d => Path.Combine(d, asmName)).FirstOrDefault(File.Exists) ?? Path.Combine(acad, asmName);
var asm = mlc.LoadFromAssemblyPath(asmPath);
Console.WriteLine($"# {Path.GetFileName(asmPath)} {asm.GetName().Version} (Civil 3D {year})");

Type[] all;
try { all = asm.GetTypes(); }
catch (ReflectionTypeLoadException e) { all = e.Types.Where(t => t != null).ToArray()!; }

if (find != null)
{
    foreach (var t in all.Where(t => t.FullName!.Contains(find, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.FullName))
        Console.WriteLine(t.FullName);
    return 0;
}

const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
foreach (var tn in types)
{
    var t = asm.GetType(tn);
    if (t == null) { Console.WriteLine("NOTFOUND " + tn); continue; }
    string baseName;
    try { baseName = t.BaseType?.FullName ?? ""; } catch (Exception e) { baseName = "? " + e.GetType().Name; }
    Console.WriteLine($"== {t.FullName} : {baseName}");
    foreach (var m in t.GetMembers(Flags).OrderBy(m => m.MemberType).ThenBy(m => m.Name))
    {
        try
        {
            switch (m)
            {
                case MethodInfo mi when !mi.IsSpecialName:
                    Console.WriteLine($"  M {(mi.IsStatic ? "static " : "")}{Name(mi.ReturnType)} {mi.Name}({Params(mi.GetParameters())})");
                    break;
                case PropertyInfo pi:
                    var set = pi.SetMethod != null && pi.SetMethod.IsPublic;
                    var stat = (pi.GetMethod ?? pi.SetMethod)!.IsStatic ? "static " : "";
                    var idx = pi.GetIndexParameters();
                    Console.WriteLine($"  P {stat}{Name(pi.PropertyType)} {pi.Name}{(idx.Length > 0 ? "[" + Params(idx) + "]" : "")} {{get;{(set ? "set;" : "")}}}");
                    break;
                case ConstructorInfo ci:
                    Console.WriteLine($"  C ({Params(ci.GetParameters())})");
                    break;
                case FieldInfo fi when t.IsEnum && fi.IsStatic:
                    Console.WriteLine("  E " + fi.Name);
                    break;
                case FieldInfo fi:
                    Console.WriteLine($"  F {(fi.IsStatic ? "static " : "")}{Name(fi.FieldType)} {fi.Name}");
                    break;
                case EventInfo ei:
                    var invoke = ei.EventHandlerType?.GetMethod("Invoke");
                    Console.WriteLine($"  V {ei.Name}({(invoke == null ? "?" : Params(invoke.GetParameters()))})");
                    break;
            }
        }
        catch (Exception e) { Console.WriteLine($"  ? {m.Name} {e.Message}"); }
    }
}
return 0;

static string Params(ParameterInfo[] ps) => string.Join(", ", ps.Select(p => Name(p.ParameterType) + " " + p.Name));
static string Name(Type t) => t.IsGenericType
    ? t.Name.Split('`')[0] + "<" + string.Join(",", t.GetGenericArguments().Select(Name)) + ">"
    : t.Name;
