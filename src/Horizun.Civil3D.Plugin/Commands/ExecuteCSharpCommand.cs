// -----------------------------------------------------------------------------
// horizun_c3d_execute_csharp - Roslyn script host (escape hatch, OFF by default).
//
// Gate: profile unsafe_code AND enable_execute_csharp (checked by the server and
// again by the dispatcher). query mode always aborts the transaction; execute
// commits as one undoable command (the dispatcher runs UnsafeCode in command
// context). Results are SELF-REPORTED: the host verifies nothing a script does.
// A query script can use doc/db to commit its own transaction or write files;
// aborting the transaction supplied as `tr` does not roll back those effects.
//
// Compiled scripts are cached by source hash: the first compile of a session
// takes seconds; repeats are immediate.
// -----------------------------------------------------------------------------
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Horizun.Civil3D.Core;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Horizun.Civil3D.Plugin.Commands;

/// <summary>Globals visible to scripts.</summary>
public sealed class HzScriptGlobals
{
    public Document doc = null!;
    public CivilDocument civilDoc = null!;
    public Database db = null!;
    public Transaction tr = null!;
    public JsonObject args = new();
    internal readonly StringBuilder Output = new();

    public void Print(object? value)
    {
        if (Output.Length < 200_000) Output.AppendLine(value?.ToString() ?? "null");
    }

    public T Open<T>(ObjectId id, OpenMode mode = OpenMode.ForRead) where T : DBObject => (T)tr.GetObject(id, mode);

    public Autodesk.Civil.DatabaseServices.Surface Surface(string name, OpenMode mode = OpenMode.ForRead)
    {
        foreach (ObjectId id in civilDoc.GetSurfaceIds())
            if (tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Surface s && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
                return mode == OpenMode.ForRead ? s : (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(id, mode);
        throw new ArgumentException("No surface named '" + name + "'.");
    }
}

internal sealed class ExecuteCSharpCommand : ICommand
{
    public string Name => "execute_csharp";
    private const int MaxCodeChars = 100_000;
    private static readonly ConcurrentDictionary<string, Script<object>> Cache = new();

    private static readonly string[] Imports =
    {
        "System", "System.Linq", "System.Collections.Generic", "System.Text.Json.Nodes",
        "Autodesk.AutoCAD.ApplicationServices", "Autodesk.AutoCAD.DatabaseServices", "Autodesk.AutoCAD.Geometry",
        "Autodesk.Civil", "Autodesk.Civil.ApplicationServices", "Autodesk.Civil.DatabaseServices",
    };

    private static ScriptOptions Options()
    {
        var refs = new[]
        {
            typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(JsonNode).Assembly,
            typeof(Database).Assembly, typeof(Document).Assembly, typeof(Autodesk.AutoCAD.Geometry.Point3d).Assembly,
            typeof(CivilDocument).Assembly, typeof(HzScriptGlobals).Assembly,
        }.Distinct();
        return ScriptOptions.Default.WithReferences(refs).WithImports(Imports).WithEmitDebugInformation(false);
    }

    public CommandResult Execute(CommandContext ctx)
    {
        var code = Hz.Str(ctx.Args, "code") ?? "";
        if (code.Length == 0 || code.Length > MaxCodeChars)
            throw new HzRefusal(ErrorCodes.InvalidInput, "code must be 1-" + MaxCodeChars + " characters. Nothing ran.");
        var mode = Hz.Str(ctx.Args, "mode") ?? "query";
        if (mode is not ("query" or "execute")) throw new HzRefusal(ErrorCodes.InvalidInput, "mode must be query or execute. Nothing ran.");
        var execute = mode == "execute";
        var doc = ctx.Document(forWrite: execute);

        var sw = Stopwatch.StartNew();
        var key = Hz.Sha256Hex(code);
        var cached = Cache.TryGetValue(key, out var script);
        if (!cached)
        {
            script = CSharpScript.Create<object>(code, Options(), typeof(HzScriptGlobals));
            var errors = script.Compile().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Take(15).Select(d => d.ToString()).ToList();
            if (errors.Count > 0)
                throw new HzRefusal(ErrorCodes.InvalidInput, "The script does not compile. Nothing ran.", new JsonObject { ["compile_errors"] = Hz.Strings(errors) });
            Cache[key] = script;
        }
        var compileMs = sw.ElapsedMilliseconds;

        var globals = new HzScriptGlobals { doc = doc, civilDoc = CommandContext.Civil(doc), db = doc.Database, args = (JsonObject?)ctx.Args["args"]?.DeepClone() ?? new JsonObject() };
        object? value;
        try
        {
            value = execute
                ? ctx.Write(doc, "HZ_CSHARP", tr => { globals.tr = tr; return Run(script!, globals); }, recordUndo: false)
                : ctx.Read(doc, tr => { globals.tr = tr; return Run(script!, globals); });
        }
        catch (ScriptFailure f)
        {
            throw new HzRefusal(ErrorCodes.TransactionFailed,
                "The script threw " + f.InnerException!.GetType().Name + ": " + f.InnerException.Message +
                ". The bridge-managed transaction was rolled back; independent transactions and external effects may have persisted. Inspect the drawing and files before retrying.",
                new JsonObject { ["output"] = globals.Output.Length > 0 ? globals.Output.ToString() : null });
        }
        finally
        {
            // Arbitrary code may have committed an independent transaction even
            // in query mode or before throwing. Invalidate previous plans/UNDO.
            Civil.DrawingRevision.Bump(doc);
            Civil.DrawingRevision.ClearLastWrite(doc);
        }

        JsonNode? returned;
        string? serializeNote = null;
        try { returned = value is JsonNode n ? n.DeepClone() : JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object), Hz.Compact); }
        catch (Exception e) { returned = JsonValue.Create(value?.ToString()); serializeNote = "Return value is not JSON-serialisable (" + e.GetType().Name + "); its ToString() is shown."; }

        return CommandResult.Ok(new JsonObject
        {
            ["tool"] = ctx.Tool.Name,
            ["mode"] = mode,
            ["document"] = doc.Name,
            ["committed"] = execute ? true : null,
            ["bridge_transaction_committed"] = execute,
            ["returned"] = returned,
            ["return_note"] = serializeNote,
            ["output"] = globals.Output.Length > 0 ? globals.Output.ToString() : null,
            ["compile_ms"] = compileMs,
            ["compiled_from_cache"] = cached,
            ["total_ms"] = sw.ElapsedMilliseconds,
            ["evidence_status"] = "self_reported_unverified",
            ["host_verified"] = false,
            ["warning"] = execute
                ? "The bridge-managed transaction committed, but independent drawing or file effects may also have occurred. Nothing was verified by the bridge, and one Civil 3D UNDO is not guaranteed to reverse all effects. Re-read before reporting completion."
                : "query mode: the bridge-managed transaction was aborted. A script can still commit its own transaction or change files and settings; inspect those effects before reporting this as read-only.",
        });
    }

    private sealed class ScriptFailure : Exception
    {
        public ScriptFailure(Exception inner) : base(inner.Message, inner) { }
    }

    private static object? Run(Script<object> script, HzScriptGlobals g)
    {
        try { return script.RunAsync(g).GetAwaiter().GetResult().ReturnValue; }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: not null } t ? t.InnerException! : e;
            throw new ScriptFailure(inner); // propagates out of Read/Write: the transaction is disposed without commit
        }
    }
}
