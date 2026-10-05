// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - FIFO ownership of Civil 3D's single UI thread.
// (Pattern from Horizun Revit MCP's RequestGate.)
//
// Civil 3D executes one API operation at a time on its main thread. That does
// not mean every later caller must fail: it means a BOUNDED queue in which each
// request owns its own completion signal, is taken exactly once, and can be
// removed while it is still waiting. Once a request starts, nothing can
// interrupt it - so cancellation is only ever claimed for work that never ran.
//
// Prevents four silent failures:
//   * STALE WAKE        one caller can never receive another caller's result
//   * DOUBLE EXECUTION  a request is taken exactly once
//   * ZOMBIE START      a timed-out or cancelled entry is removed before it runs
//   * UNBOUNDED PROMISE a retry storm cannot queue hours of future edits
//
// No Autodesk references: sequencing is tested without Civil 3D.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public sealed class RequestGate
{
    public const int DefaultCapacity = 16;

    public sealed class Request
    {
        public long Ticket { get; internal set; }
        public string? WireId { get; internal set; }
        public string Command { get; internal set; } = "";
        public JsonObject Params { get; internal set; } = new();
        public DateTime QueuedUtc { get; internal set; }
        public DateTime StartedUtc { get; internal set; }
        public DateTime FinishedUtc { get; internal set; }
        public int AheadAtAdmission { get; internal set; }

        public CommandResult? Result { get; set; }
        public JsonArray HostMessages { get; } = new();

        private readonly ManualResetEventSlim _done = new(false);
        public volatile bool Abandoned;
        public volatile bool Started;
        public volatile bool CancelledBeforeStart;
        internal LinkedListNode<Request>? Node;

        public bool Wait(int timeoutMs) => _done.Wait(timeoutMs);
        public bool IsDone => _done.IsSet;
        internal void Signal() => _done.Set();
    }

    private readonly object _lock = new();
    private readonly LinkedList<Request> _pending = new();
    private readonly int _capacity;
    private long _nextTicket;
    private Request? _inFlight;

    public RequestGate(int capacity = DefaultCapacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Capacity => _capacity;
    public int PendingCount { get { lock (_lock) return _pending.Count; } }
    public bool HasPending { get { lock (_lock) return _pending.Count > 0; } }
    public string? RunningCommand { get { lock (_lock) return _inFlight?.Command; } }

    /// <summary>Enqueue. Refused (null) only when the bounded queue is full: backpressure, nothing queued.</summary>
    public Request? Begin(string? wireId, string command, JsonObject parameters, out string? refusal)
    {
        lock (_lock)
        {
            if (_pending.Count >= _capacity)
            {
                refusal = "Civil 3D's command queue is full: " + _pending.Count + " of " + _capacity +
                          " waiting slots are occupied" + (_inFlight == null ? "." : " while '" + _inFlight.Command + "' runs.") +
                          " Nothing was queued and nothing ran. Wait for outstanding calls instead of retrying: " +
                          "retries take more slots and cannot make Civil 3D run in parallel.";
                return null;
            }
            var r = new Request
            {
                Ticket = ++_nextTicket,
                WireId = wireId,
                Command = command,
                Params = parameters,
                QueuedUtc = DateTime.UtcNow,
                AheadAtAdmission = _pending.Count + (_inFlight == null ? 0 : 1),
            };
            r.Node = _pending.AddLast(r);
            refusal = null;
            return r;
        }
    }

    /// <summary>Claim the oldest live entry exactly once. Null if one is already in flight or none waits.</summary>
    public Request? Take()
    {
        lock (_lock)
        {
            if (_inFlight != null) return null;
            while (_pending.Count > 0)
            {
                var node = _pending.First!;
                _pending.RemoveFirst();
                var r = node.Value;
                r.Node = null;
                if (r.Abandoned || r.CancelledBeforeStart) continue;
                r.Started = true;
                r.StartedUtc = DateTime.UtcNow;
                _inFlight = r;
                return r;
            }
            return null;
        }
    }

    public void Complete(Request r)
    {
        lock (_lock)
        {
            if (_inFlight == r) _inFlight = null;
            r.FinishedUtc = DateTime.UtcNow;
        }
        r.Signal();
    }

    /// <summary>The waiting caller gave up. Removes the entry if it has not started; running work is only marked.</summary>
    public void Abandon(Request r)
    {
        lock (_lock)
        {
            r.Abandoned = true;
            if (!r.Started && r.Node != null)
            {
                _pending.Remove(r.Node);
                r.Node = null;
                r.CancelledBeforeStart = true;
            }
        }
    }

    /// <summary>
    /// Remove a request that has NOT started. True means it never ran. False with
    /// detail "already_running" or "not_found_or_finished" - neither may be called cancelled.
    /// </summary>
    public bool CancelQueued(string? wireId, out string detail)
    {
        if (string.IsNullOrWhiteSpace(wireId)) { detail = "no_request_id"; return false; }
        lock (_lock)
        {
            for (var node = _pending.First; node != null; node = node.Next)
            {
                var r = node.Value;
                if (!string.Equals(r.WireId, wireId, StringComparison.Ordinal)) continue;
                _pending.Remove(node);
                r.Node = null;
                r.Abandoned = true;
                r.CancelledBeforeStart = true;
                r.Result = CommandResult.Fail(ErrorCodes.Timeout,
                    "Cancelled while waiting in Civil 3D's queue. It NEVER STARTED: nothing was executed and nothing was written.");
                r.Signal();
                detail = "cancelled_before_start";
                return true;
            }
            detail = _inFlight != null && string.Equals(_inFlight.WireId, wireId, StringComparison.Ordinal)
                ? "already_running"
                : "not_found_or_finished";
            return false;
        }
    }

    /// <summary>Fail and wake everything still waiting (shutdown).</summary>
    public int FailQueued(string reason)
    {
        var wake = new List<Request>();
        lock (_lock)
        {
            while (_pending.Count > 0)
            {
                var r = _pending.First!.Value;
                _pending.RemoveFirst();
                r.Node = null;
                r.CancelledBeforeStart = true;
                r.Result = CommandResult.Fail(ErrorCodes.Internal, reason);
                wake.Add(r);
            }
        }
        foreach (var r in wake) r.Signal();
        return wake.Count;
    }

    public JsonObject Observe()
    {
        lock (_lock)
        {
            var o = new JsonObject
            {
                ["capacity"] = _capacity,
                ["waiting"] = _pending.Count,
                ["running"] = _inFlight?.Command,
            };
            if (_inFlight != null)
                o["running_for_ms"] = (long)Math.Max(0, (DateTime.UtcNow - _inFlight.StartedUtc).TotalMilliseconds);
            o["waiting_commands"] = Hz.Strings(_pending.Select(r => r.Command));
            return o;
        }
    }
}
