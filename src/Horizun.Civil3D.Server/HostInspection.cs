using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Server;

/// <summary>Inspect managed PE metadata only. Never loads or executes Autodesk code.</summary>
public static class HostInspection
{
    public sealed record AssemblyInfo(string Name, Version Version, string Framework);

    public static AssemblyInfo ReadAssembly(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024 * 1024) throw new InvalidDataException("Managed assembly exceeds the inspection limit.");
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var assembly = metadata.GetAssemblyDefinition();
        string? framework = null;
        foreach (var handle in assembly.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Namespace) != "System.Runtime.Versioning" ||
                metadata.GetString(type.Name) != "TargetFrameworkAttribute") continue;
            if (framework != null) throw new InvalidDataException("Duplicate TargetFrameworkAttribute.");
            var blob = metadata.GetBlobReader(attribute.Value);
            if (blob.ReadUInt16() != 1) throw new InvalidDataException("Invalid framework attribute.");
            framework = blob.ReadSerializedString();
        }
        return new(metadata.GetString(assembly.Name), assembly.Version,
            framework ?? throw new InvalidDataException("Assembly has no TargetFrameworkAttribute."));
    }

    public static JsonObject Inspect(string root, int year)
    {
        var autocad = ReadAssembly(Path.Combine(root, "acmgd.dll"));
        var civil = ReadAssembly(Path.Combine(root, "C3D", "AeccDbMgd.dll"));
        return InspectAssemblies(autocad, civil, year);
    }

    public static JsonObject InspectAssemblies(AssemblyInfo autocad, AssemblyInfo civil, int year)
    {
        var expected = year switch { 2024 => (24, 3), 2025 => (25, 0), 2026 => (25, 1), 2027 => (26, 0), _ => throw new ArgumentException("Unknown Civil 3D year.") };
        if (!autocad.Name.Equals("acmgd", StringComparison.OrdinalIgnoreCase) || civil.Name != "AeccDbMgd" ||
            (autocad.Version.Major, autocad.Version.Minor) != expected)
            throw new InvalidDataException("Autodesk assembly identity/year mismatch; another year's DLLs cannot substitute.");
        var runtime = autocad.Framework switch
        {
            ".NETFramework,Version=v4.7" => "net48",
            ".NETFramework,Version=v4.7.1" => "net48",
            ".NETFramework,Version=v4.7.2" => "net48",
            ".NETFramework,Version=v4.8" => "net48",
            ".NETCoreApp,Version=v8.0" => "net8",
            ".NETCoreApp,Version=v10.0" => "net10",
            _ => throw new InvalidDataException("Unknown Autodesk target framework: " + autocad.Framework),
        };
        var compatibleCivil = runtime switch
        {
            "net48" => civil.Framework is ".NETFramework,Version=v4.7" or ".NETFramework,Version=v4.7.1" or ".NETFramework,Version=v4.7.2" or ".NETFramework,Version=v4.8",
            "net8" => civil.Framework == ".NETCoreApp,Version=v8.0",
            "net10" => civil.Framework is ".NETCoreApp,Version=v8.0" or ".NETCoreApp,Version=v10.0",
            _ => false,
        };
        if (!compatibleCivil) throw new InvalidDataException("AutoCAD and Civil 3D reference frameworks are incompatible.");
        if ((year == 2024) != (runtime == "net48")) throw new InvalidDataException("Year/runtime mismatch.");
        if (year == 2027 && runtime != "net10") throw new InvalidDataException("Civil 3D 2027 requires net10.");
        return new() { ["year"] = year, ["runtime"] = runtime, ["target_framework"] = autocad.Framework, ["civil_target_framework"] = civil.Framework,
            ["autocad_assembly_version"] = autocad.Version.ToString(), ["civil_assembly_version"] = civil.Version.ToString() };
    }

    /// <summary>
    /// Whether a prebuilt add-in (its host-builds.json record) can load on the inspected host. Year, runtime and
    /// frameworks must be equal. Autodesk assemblies are not strong-named and Civil's carry the update build
    /// (13.7.0.&lt;build&gt;), so the same release with an equal or NEWER build binds; an older build may not.
    /// </summary>
    public static (bool Compatible, string Reason) Match(JsonObject built, JsonObject installed)
    {
        static string? Text(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        int? builtYear = built["year"] is JsonValue by && by.TryGetValue<int>(out var b) ? b : null;
        int? installedYear = installed["year"] is JsonValue iy && iy.TryGetValue<int>(out var i) ? i : null;
        if (builtYear == null || builtYear != installedYear)
            return (false, "Build record year " + (builtYear?.ToString() ?? "missing") + " does not match Civil 3D " + (installedYear?.ToString() ?? "missing") + ".");
        var year = builtYear.Value;
        foreach (var key in new[] { "runtime", "target_framework", "civil_target_framework" })
        {
            var want = Text(built, key);
            var have = Text(installed, key);
            if (want == null || have == null || !string.Equals(want, have, StringComparison.Ordinal))
                return (false, "Civil 3D " + year + " " + key + " is " + (have ?? "unreadable") + "; this package was built for " + (want ?? "unknown") + ".");
        }
        foreach (var (key, dll) in new[] { ("autocad_assembly_version", "acmgd"), ("civil_assembly_version", "AeccDbMgd") })
        {
            if (!Version.TryParse(Text(built, key), out var want) || !Version.TryParse(Text(installed, key), out var have))
                return (false, "Civil 3D " + year + " " + dll + " version is unreadable (installed " + (Text(installed, key) ?? "missing") + ", package " + (Text(built, key) ?? "missing") + ").");
            if (want.Major != have.Major || want.Minor != have.Minor)
                return (false, "Civil 3D " + year + " " + dll + " " + have + " is a different release than this package's " + want + ".");
            if (have < want)
                return (false, "Civil 3D " + year + " has " + dll + " " + have + "; this package needs " + want + " or later. Install the latest Civil 3D " + year + " update, or obtain a package built for this update.");
        }
        return (true, "Civil 3D " + year + " " + Text(installed, "civil_assembly_version") + " is compatible with this package (built against " + Text(built, "civil_assembly_version") + ").");
    }
}
