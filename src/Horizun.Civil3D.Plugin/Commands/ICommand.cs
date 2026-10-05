// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - what a plug-in command is, and what it may use.
//
// A command runs on Civil 3D's main thread, in application context, one at a
// time. It gets a CommandContext that owns the rules every command shares:
//
//   * which drawing      Document(): explicit target, never a guess; writes
//                        REQUIRE target_document and it must be the active one
//   * reads              Read(): document lock + transaction that is ABORTED
//   * writes             Write(): document lock with an undo label + commit;
//                        a commit that fails is an error, never a success
//   * proof              Verify(): a NEW transaction after the commit, used to
//                        re-read what was written (see VerificationSet)
//   * confirmation       Rehearse()/RequireConfirmation(): dry run + token
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Horizun.Civil3D.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin.Commands;

internal interface ICommand
{
    /// <summary>Wire command name; must match Contract.PluginCommands.</summary>
    string Name { get; }
    CommandResult Execute(CommandContext ctx);
}

internal sealed class CommandContext
{
    public required JsonObject Args { get; init; }
    public required ToolContract Tool { get; init; }
    public required Settings Settings { get; init; }
    public required ConfirmationStore Confirmations { get; init; }
    public required JsonArray HostMessages { get; init; }
    public required int Year { get; init; }

    public string? Action => Hz.Str(Args, "action");

    public string RequireAction(params string[] allowed)
    {
        var a = Action;
        if (a == null || !allowed.Contains(a))
            throw new HzRefusal(ErrorCodes.InvalidInput,
                "action must be one of: " + string.Join(", ", allowed) + (a == null ? "." : " (got '" + a + "')."));
        return a;
    }

    // ---- target drawing ---------------------------------------------------

    public static bool NameMatches(Document doc, string target)
    {
        var full = doc.Name ?? "";
        var t = target.Trim();
        return string.Equals(full, t, StringComparison.OrdinalIgnoreCase)
               || string.Equals(Path.GetFileName(full), t, StringComparison.OrdinalIgnoreCase)
               || string.Equals(Path.GetFileNameWithoutExtension(full), t, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The drawing this call operates on. Reads default to the active drawing;
    /// writes must name it. A named drawing that is not the active one is refused
    /// (Phase 0 never switches the user's active window).
    /// </summary>
    public Document Document(bool forWrite)
    {
        var dm = AcApp.DocumentManager;
        var active = dm.MdiActiveDocument
                     ?? throw new HzRefusal(ErrorCodes.NoDocument, "No drawing is open in Civil 3D.");
        var target = Hz.Str(Args, "target_document");
        if (string.IsNullOrWhiteSpace(target))
        {
            if (forWrite)
                throw new HzRefusal(ErrorCodes.InvalidInput,
                    "Writes must name the drawing: pass target_document (e.g. '" + Path.GetFileName(active.Name) +
                    "'). It must be the ACTIVE drawing. Nothing was changed.",
                    new JsonObject { ["active_document"] = active.Name });
            return active;
        }
        if (NameMatches(active, target)) return active;

        var open = new JsonArray();
        var exists = false;
        foreach (Document d in dm)
        {
            open.Add(JsonValue.Create(d.Name));
            if (NameMatches(d, target)) exists = true;
        }
        throw new HzRefusal(ErrorCodes.DocumentMismatch,
            exists
                ? "'" + target + "' is open but is NOT the active drawing ('" + Path.GetFileName(active.Name) + "'). The bridge " +
                  "never switches your window. Activate it in Civil 3D and call again. Nothing ran."
                : "'" + target + "' is not open in this Civil 3D. Nothing ran.",
            new JsonObject { ["active_document"] = active.Name, ["open_documents"] = open });
    }

    public static CivilDocument Civil(Document doc) => CivilDocument.GetCivilDocument(doc.Database);

    /// <summary>Stable key of a drawing for confirmation tokens: path + database fingerprint.</summary>
    public static string DocumentKey(Document doc)
    {
        string fp;
        try { fp = doc.Database.FingerprintGuid ?? ""; } catch { fp = ""; }
        return (doc.Name ?? "").ToLowerInvariant() + "|" + fp;
    }

    // ---- transactions -----------------------------------------------------

    public T Read<T>(Document doc, Func<Transaction, T> body)
    {
        using var _ = doc.LockDocument();
        using var tr = doc.Database.TransactionManager.StartTransaction();
        try { return body(tr); }
        finally { tr.Abort(); }
    }

    /// <summary>Lock (one undo step, labelled), run, commit. A failed commit throws.</summary>
    public T Write<T>(Document doc, string undoLabel, Func<Transaction, T> body)
    {
        if (doc.IsReadOnly)
            throw new HzRefusal(ErrorCodes.ReadOnlyDocument,
                "'" + Path.GetFileName(doc.Name) + "' is open read-only. Nothing was changed.");
        T result;
        Horizun.Civil3D.Plugin.Civil.DrawingRevision.StartRecording();
        (List<ObjectId> Appended, List<ObjectId> Modified) touched;
        try
        {
            using var _ = doc.LockDocument(DocumentLockMode.Write, undoLabel, undoLabel, false);
            using var tr = doc.Database.TransactionManager.StartTransaction();
            result = body(tr);
            tr.Commit();
        }
        finally { touched = Horizun.Civil3D.Plugin.Civil.DrawingRevision.StopRecording(); }
        Horizun.Civil3D.Plugin.Civil.DrawingRevision.Bump(doc); // our own committed edit: older tokens must go stale
        // Remember it for undo_last: only undoable while the drawing has not changed since this exact moment.
        Horizun.Civil3D.Plugin.Civil.DrawingRevision.SetLastWrite(doc, new(Horizun.Civil3D.Plugin.Civil.DrawingRevision.Capture(doc),
            Tool.Name, Action ?? "", undoLabel, touched.Appended, touched.Modified.Count, DateTime.UtcNow));
        return result;
    }

    /// <summary>A NEW transaction after the commit, to re-read what the drawing really holds.</summary>
    public T Verify<T>(Document doc, Func<Transaction, T> body) => Read(doc, body);

    // ---- dry run / confirmation ------------------------------------------

    public bool DryRun => Hz.Bool(Args, "dry_run") ?? true;

    private string Operation => Tool.Name + ":" + (Action ?? "");

    /// <summary>Stamp a dry-run reply with a single-use token bound to drawing + request + plan.</summary>
    public JsonObject Rehearse(Document doc, JsonObject data, JsonNode plan)
    {
        var (token, expires) = Confirmations.Issue(Operation, DocumentKey(doc), ConfirmationStore.RequestHash(Args),
            ConfirmationStore.PlanFingerprint(plan));
        data["dry_run"] = true;
        data["committed"] = false;
        data["plan"] = plan.DeepClone();
        data["confirmation_token"] = token;
        data["expires_at"] = expires.ToString("o");
        data["next_step"] = "Nothing was changed. To apply exactly this plan, call again with the same arguments, " +
                            "dry_run=false and this confirmation_token (single use, 10 minutes).";
        return data;
    }

    /// <summary>Refuse unless the token matches this drawing, this request and the plan recomputed NOW.</summary>
    public void RequireConfirmation(Document doc, JsonNode planNow)
    {
        var check = Confirmations.Validate(Hz.Str(Args, "confirmation_token"), Operation, DocumentKey(doc),
            ConfirmationStore.RequestHash(Args), ConfirmationStore.PlanFingerprint(planNow));
        if (!check.Ok)
            throw new HzRefusal(
                check.State == ConfirmationState.Missing ? ErrorCodes.ConfirmationRequired : ErrorCodes.ConfirmationRefused,
                check.Message ?? "Confirmation refused.",
                new JsonObject { ["confirmation_state"] = check.StateName });
    }
}
