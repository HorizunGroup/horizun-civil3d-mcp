namespace Horizun.Civil3D.Core;

public static partial class Contract
{
    private static ToolContract AuditTool() => new()
    {
        Name = "horizun_c3d_audit",
        Command = "audit",
        Title = "Audit drawing units, references and Civil 3D object currency",
        Effect = ToolEffect.Read,
        Description =
            "Read-only snapshot of the active Civil 3D drawing: file state, drawing and display units, coordinate " +
            "system, external-reference status, and counts for surfaces, alignments, profiles, corridors, feature " +
            "lines, sites, parcels and gravity-network objects. Reports stale/invalid data-shortcut references and " +
            "out-of-date surfaces and corridors where the installed API exposes those properties. Every type is " +
            "scanned independently; an unreadable type or object is counted separately and never treated as " +
            "healthy. sample_limit bounds the detailed examples, not the count. No design-standard compliance " +
            "or geometry quality is inferred.",
        InputSchemaJson = """
            {"type":"object","properties":{
              "target_document":{"type":"string","description":"Active drawing name or full path; defaults to active drawing."},
              "sample_limit":{"type":"integer","minimum":0,"maximum":200,"default":50,"description":"Maximum detailed problem examples; all supported objects are still counted."}
            },"additionalProperties":false}
            """,
    };
}
