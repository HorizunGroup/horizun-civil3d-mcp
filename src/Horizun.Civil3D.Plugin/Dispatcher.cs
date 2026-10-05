// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - one request at a time, honestly reported.
//
// Pipe thread  : Invoke() checks permission, enqueues in the bounded RequestGate,
//                kicks the UI pump and WAITS, watching the UI snapshot:
//                  - Civil 3D busy (command / dialog) for 2 s before our request
//                    started  -> removed from the queue, refused as "busy",
//                    NOTHING RAN. The user's work is never cancelled.
//                  - main thread not refreshing for 10 s -> "not responding",
//                    removed if not started.
//                  - overall timeout -> says whether it NEVER STARTED or is
//                    STILL RUNNING (Civil 3D cannot interrupt running work).
// Main thread  : RunNext() takes exactly one request, re-checks permission,
//                executes the command and completes it.
// -----------------------------------------------------------------------------
using System.Diagnostics;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Commands;

namespace Horizun.Civil3D.Plugin;

internal sealed class Dispatcher
{
    private const int SliceMs = 250;
    private const double BusyGraceSeconds = 2.0;
    private const double NotRespondingSeconds = 10.0;

    private readonly Dictionary<string, ICommand> _commands = new(StringComparer.Ordinal);
    private readonly ConfirmationStore _confirmations = new();
    private readonly int _year;

    public RequestGate Gate { get; } = new();
    public string? LastCommand { get; private set; }
    public DateTime? LastCommandUtc { get; private set; }

    public Dispatcher(int year) => _year = year;

    public void Register(ICommand c) => _commands[c.Name] = c;

    public IReadOnlyCollection<string> CommandNames => _commands.Keys;

    /// <summary>Called on the pipe thread. Blocks until the command completes or is refused.</summary>
    public JsonObject Invoke(string? wireId, string command, JsonObject parameters, int timeoutMs)
    {
        var contract = Contract.FindByCommand(command);
        if (contract == null || !_commands.ContainsKey(command))
            return Wire.Reply(wireId, CommandResult.Fail(ErrorCodes.Unsupported,
                "Unknown command '" + command + "'. This plug-in implements: " + string.Join(", ", _commands.Keys) + ". Nothing ran."));

        var settings = Settings.Load();
        var refusal = settings.Refusal(contract.Name, contract.EffectFor(parameters));
        if (refusal != null)
            return Wire.Reply(wireId, CommandResult.Fail(ErrorCodes.PermissionDenied, refusal));

        var r = Gate.Begin(wireId, command, parameters, out var full);
        if (r == null) return Wire.Reply(wireId, CommandResult.Fail(ErrorCodes.QueueFull, full!));

        UiPump.Kick();
        var sw = Stopwatch.StartNew();
        double busyFor = 0;
        while (!r.Wait(SliceMs))
        {
            if (!r.Started)
            {
                var snap = UiPump.State;
                busyFor = snap.Quiescent && snap.AgeSeconds < 2 ? 0 : busyFor + SliceMs / 1000.0;
                if (!snap.Quiescent && snap.AgeSeconds < 2 && busyFor >= BusyGraceSeconds && TryWithdraw(r))
                    return Wire.Reply(wireId, CommandResult.Fail(ErrorCodes.Busy,
                        "Civil 3D is busy: " + snap.BusyDescription() + ". The request was withdrawn and NOTHING RAN. " +
                        "The bridge never sends ESC or cancels your work: finish or cancel it in Civil 3D, then call again.",
                        new JsonObject { ["cmdactive"] = snap.CmdActive, ["cmdnames"] = snap.CmdNames, ["modal_open"] = snap.ModalOpen }),
                        null, Queue(r, sw));
                if (snap.AgeSeconds >= NotRespondingSeconds && sw.Elapsed.TotalSeconds >= NotRespondingSeconds && TryWithdraw(r))
                    return Wire.Reply(wireId, CommandResult.Fail(ErrorCodes.NotResponding,
                        "Civil 3D's main thread has not responded for " + (int)snap.AgeSeconds + " s (a long operation or a " +
                        "hang). The request was withdrawn and NOTHING RAN."), null, Queue(r, sw));
            }
            if (sw.ElapsedMilliseconds >= timeoutMs)
            {
                if (TryWithdraw(r))
                    return Wire.Reply(wireId, CommandResult.Fail(ErrorCodes.Timeout,
                        "Timed out after " + timeoutMs / 1000 + " s while WAITING in the queue. It NEVER STARTED: nothing ran."),
                        null, Queue(r, sw));
                Gate.Abandon(r);
                return Wire.Reply(wireId, CommandResult.Fail(ErrorCodes.Timeout,
                    "Timed out after " + timeoutMs / 1000 + " s, but the command IS STILL RUNNING inside Civil 3D and cannot be " +
                    "interrupted. Its result is unknown: check the drawing (horizun_c3d_query) before repeating any write."),
                    null, Queue(r, sw));
            }
        }

        var result = r.Result ?? CommandResult.Fail(ErrorCodes.Internal, "The command finished without a result.");
        return Wire.Reply(wireId, result, r.HostMessages, Queue(r, sw));
    }

    /// <summary>Remove a request that has not started. False if it started meanwhile.</summary>
    private bool TryWithdraw(RequestGate.Request r)
    {
        Gate.Abandon(r);
        return r.CancelledBeforeStart;
    }

    private JsonObject Queue(RequestGate.Request r, Stopwatch sw)
    {
        var waited = r.Started ? (r.StartedUtc - r.QueuedUtc).TotalMilliseconds : sw.Elapsed.TotalMilliseconds;
        return new JsonObject
        {
            ["queued"] = r.AheadAtAdmission > 0,
            ["ahead_at_admission"] = r.AheadAtAdmission,
            ["waited_ms"] = (long)Math.Max(0, waited),
            ["total_ms"] = sw.ElapsedMilliseconds,
            ["capacity"] = Gate.Capacity,
        };
    }

    /// <summary>Main thread, application context, Civil 3D quiescent. Runs at most one request.</summary>
    public void RunNext(UiSnapshot snap)
    {
        var r = Gate.Take();
        if (r == null) return;

        // Live finding (v0.3.3, fixture): edits made from APPLICATION context under LockDocument are
        // NOT recorded on Civil 3D's undo stack - "one UNDO reverts this" was false. Applied writes
        // therefore run inside a COMMAND context (ExecuteInCommandContextAsync, called from application
        // context as the API requires), which AutoCAD records as one undoable command. Reads and dry
        // runs stay in application context. The request stays in flight until the callback completes it.
        var contract = Contract.FindByCommand(r.Command);
        var effect = contract?.EffectFor(r.Params);
        var appliedWrite = effect == ToolEffect.UnsafeCode // C# has no dry run: always one undoable command
                           || (effect >= ToolEffect.SafeWrite && Hz.Bool(r.Params, "dry_run") == false);
        if (appliedWrite)
        {
            try
            {
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.ExecuteInCommandContextAsync(_ =>
                {
                    RunCore(r);
                    return Task.CompletedTask;
                }, null);
            }
            catch (Exception e)
            {
                Log.Error("could not enter command context for " + r.Command, e);
                r.Result = CommandResult.Fail(ErrorCodes.Busy,
                    "Civil 3D could not start the write as an undoable command (" + e.Message + "). NOTHING RAN.");
                Gate.Complete(r);
            }
            return;
        }
        RunCore(r);
    }

    private void RunCore(RequestGate.Request r)
    {
        try
        {
            LastCommand = r.Command;
            LastCommandUtc = DateTime.UtcNow;
            var contract = Contract.FindByCommand(r.Command)!;
            var settings = Settings.Load();
            var refusal = settings.Refusal(contract.Name, contract.EffectFor(r.Params));
            if (refusal != null)
            {
                r.Result = CommandResult.Fail(ErrorCodes.PermissionDenied, refusal);
                return;
            }
            var ctx = new CommandContext
            {
                Args = r.Params,
                Tool = contract,
                Settings = settings,
                Confirmations = _confirmations,
                HostMessages = r.HostMessages,
                Year = _year,
            };
            // Events fired by the bridge's own work are not "the drawing moved" (see DrawingRevision).
            using (Civil.DrawingRevision.Quiet())
                r.Result = _commands[r.Command].Execute(ctx);
        }
        catch (HzRefusal e)
        {
            r.Result = CommandResult.Fail(e.Code, e.Message, e.Detail);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception e)
        {
            Log.Error("command " + r.Command + " failed (AutoCAD)", e);
            r.Result = CommandResult.Fail(ErrorCodes.TransactionFailed,
                "Civil 3D refused the operation: " + e.ErrorStatus + " (" + e.Message + "). Any transaction was rolled back.",
                new JsonObject { ["error_status"] = e.ErrorStatus.ToString() });
        }
        catch (Exception e)
        {
            Log.Error("command " + r.Command + " failed", e);
            r.Result = CommandResult.Fail(ErrorCodes.Internal,
                e.GetType().Name + ": " + e.Message + ". Any open transaction was rolled back.");
        }
        finally
        {
            Gate.Complete(r);
        }
    }

    public JsonObject Status()
    {
        var snap = UiPump.State;
        return new JsonObject
        {
            ["queue"] = Gate.Observe(),
            ["ui"] = new JsonObject
            {
                ["quiescent"] = snap.Quiescent,
                ["cmdactive"] = snap.CmdActive,
                ["cmdnames"] = snap.CmdNames,
                ["modal_open"] = snap.ModalOpen,
                ["active_document"] = snap.ActiveDocument,
                ["snapshot_age_s"] = Math.Round(snap.AgeSeconds, 2),
                ["busy"] = snap.Quiescent ? null : snap.BusyDescription(),
            },
            ["last_command"] = LastCommand,
            ["last_command_utc"] = LastCommandUtc?.ToString("o"),
            ["outstanding_confirmations"] = _confirmations.OutstandingCount,
        };
    }
}
