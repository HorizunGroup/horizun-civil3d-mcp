// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - the write contract as one reusable flow.
//
//   plan    (read transaction)  resolve everything, refuse anything ambiguous,
//                               describe exactly what will change
//   dry run                     plan + single-use token, nothing changed
//   apply   (one transaction)   requires the token; runs in command context
//                               (the dispatcher) = one UNDO step
//   verify  (NEW transaction)   re-read what the drawing really holds
//
// Tools pass three lambdas and share state through closures.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal static class WriteFlow
{
    // These actions change files or Civil 3D's data-shortcut environment outside
    // the DWG undo stack. A DWG UNDO cannot reverse the full operation.
    private static bool HasOnlyDrawingEffects(CommandContext ctx) =>
        !(ctx.Tool.Name == "horizun_c3d_exchange" ||
          (ctx.Tool.Name == "horizun_c3d_layouts" && ctx.Action == "plot_pdf") ||
          (ctx.Tool.Name == "horizun_c3d_points" && ctx.Action is "export_csv" or "export_editable_csv"));

    public static JsonObject Data(CommandContext ctx, Document doc) => new()
    {
        ["tool"] = ctx.Tool.Name,
        ["action"] = ctx.Action,
        ["document"] = doc.Name,
        ["units"] = DrawingInfo.Units(CommandContext.Civil(doc), doc.Database),
    };

    public static CommandResult Run(CommandContext ctx, string undoLabel,
                                    Action<Document, Transaction, JsonObject> plan,
                                    Action<Document, Transaction> apply,
                                    Action<Document, Transaction, VerificationSet, JsonObject> verify)
    {
        var doc = ctx.Document(forWrite: true);
        if (doc.IsReadOnly) throw new HzRefusal(ErrorCodes.ReadOnlyDocument, "The drawing is read-only. Nothing changed.");
        using var operationLock = doc.LockDocument(DocumentLockMode.Write, undoLabel, undoLabel, false);
        var data = Data(ctx, doc);
        var p = new JsonObject { ["action"] = ctx.Action, ["drawing_revision"] = DrawingRevision.Capture(doc) };
        ctx.Read(doc, tr => { plan(doc, tr, p); return 0; });
        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, p));
        ctx.RequireConfirmation(doc, p);
        var stage = ctx.Tool.Name + ":" + ctx.Action;
        Log.Info("apply start " + stage);
        var drawingOnly = HasOnlyDrawingEffects(ctx);
        try
        {
            ctx.Write(doc, undoLabel, tr => { apply(doc, tr); Log.Info("apply body done, committing " + stage); return 0; },
                recordUndo: drawingOnly);
        }
        catch (Exception e) when (!drawingOnly)
        {
            Log.Error("external apply outcome unknown " + stage, e);
            data["dry_run"] = false;
            data["committed"] = null;
            data["plan"] = p;
            data["undo"] = new JsonObject { ["available"] = false };
            data["outcome"] = "unknown_external_effects";
            return CommandResult.Fail(ErrorCodes.TransactionFailed,
                "The operation failed, but files or Civil 3D settings may already have changed. Inspect them before retrying; drawing rollback cannot reverse external effects.", data);
        }
        Log.Info("apply committed, verify start " + stage);
        var checks = new VerificationSet();
        var after = new JsonObject();
        try { ctx.Verify(doc, tr => { verify(doc, tr, checks, after); return 0; }); Log.Info("verify done " + stage); }
        catch (Exception e) { checks.Check("post-commit re-read", true, null, false, e.GetType().Name + ": " + e.Message); }
        data["dry_run"] = false;
        data["committed"] = true;
        data["plan"] = p;
        data["after"] = after;
        data["verified"] = checks.ToJson();
        data["undo"] = new JsonObject { ["available"] = false,
                ["reason"] = drawingOnly ? "Automatic undo_last is disabled; use Civil 3D native UNDO manually and inspect the result."
                    : "This operation has effects outside the drawing that Civil 3D UNDO cannot reverse." };
        return checks.AllVerified ? CommandResult.Ok(data)
            : CommandResult.Fail(ErrorCodes.VerificationFailed, "The change committed but the re-read did not verify every item. Inspect after/verified before retrying.", data);
    }

    /// <summary>Read-only action: document lock + aborted transaction, common header.</summary>
    public static CommandResult Read(CommandContext ctx, Action<Document, Transaction, JsonObject> body)
    {
        var doc = ctx.Document(forWrite: false);
        var data = Data(ctx, doc);
        ctx.Read(doc, tr => { body(doc, tr, data); return 0; });
        return CommandResult.Ok(data);
    }
}
