// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - "yes, do it", tied to a specific drawing and plan.
// (Pattern from Horizun Revit MCP's Confirmation.)
//
// A write takes dry_run=true by default: it resolves everything, returns the
// plan and issues a token. The token is bound to FOUR things and execution is
// refused unless all still hold:
//
//   * the tool and action           (a token authorises one operation)
//   * the drawing                   (the user may have switched windows)
//   * the request                   (hash of the request's own fields)
//   * the resolved plan             (fingerprint of the objects/values the dry
//                                    run found - catches a drawing that moved
//                                    while the request stayed identical)
//
// Single-use, expires after 10 minutes, held in memory: tokens do not survive a
// Civil 3D restart. Refusals name WHICH binding broke.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public enum ConfirmationState
{
    Valid,
    Missing,
    Unknown,
    Expired,
    AlreadyUsed,
    WrongOperation,
    DocumentChanged,
    RequestChanged,
    StalePlan,
}

public sealed record ConfirmationCheck(ConfirmationState State, string? Message)
{
    public bool Ok => State == ConfirmationState.Valid;
    public string StateName => State switch
    {
        ConfirmationState.Valid => "valid",
        ConfirmationState.Missing => "missing",
        ConfirmationState.Unknown => "unknown",
        ConfirmationState.Expired => "expired",
        ConfirmationState.AlreadyUsed => "already_used",
        ConfirmationState.WrongOperation => "wrong_operation",
        ConfirmationState.DocumentChanged => "document_changed",
        ConfirmationState.RequestChanged => "request_changed",
        _ => "stale_plan",
    };
}

public sealed class ConfirmationStore
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    /// <summary>Request fields that are NOT part of what gets confirmed.</summary>
    public static readonly string[] NonPlanFields = { "dry_run", "confirmation_token", "idempotency_key" };

    private sealed class Entry
    {
        public string Token = "";
        public string Operation = "";
        public string DocumentKey = "";
        public string RequestHash = "";
        public string? PlanFingerprint;
        public DateTime IssuedUtc;
        public DateTime ExpiresUtc;
        public bool Used;
    }

    private readonly Dictionary<string, Entry> _issued = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();

    public ConfirmationStore(Func<DateTime>? utcNow = null) => _now = utcNow ?? (() => DateTime.UtcNow);

    /// <summary>Hash of the request minus the non-plan fields. Array order is part of the plan.</summary>
    public static string RequestHash(JsonObject? args)
    {
        var copy = new JsonObject();
        if (args != null)
            foreach (var kv in args)
                if (!NonPlanFields.Contains(kv.Key))
                    copy[kv.Key] = kv.Value?.DeepClone();
        return Hz.Sha256Hex(Hz.Canonical(copy));
    }

    /// <summary>Fingerprint of a resolved plan (objects + values the dry run read).</summary>
    public static string PlanFingerprint(JsonNode? plan) => Hz.Sha256Hex(Hz.Canonical(plan));

    public (string Token, DateTime ExpiresUtc) Issue(string operation, string documentKey, string requestHash,
                                                     string? planFingerprint, TimeSpan? ttl = null)
    {
        var e = new Entry
        {
            Token = "hz-" + Hz.NewToken(16),
            Operation = operation,
            DocumentKey = documentKey,
            RequestHash = requestHash,
            PlanFingerprint = planFingerprint,
            IssuedUtc = _now(),
        };
        e.ExpiresUtc = e.IssuedUtc.Add(ttl ?? DefaultTtl);
        lock (_lock)
        {
            Prune();
            _issued[e.Token] = e;
        }
        return (e.Token, e.ExpiresUtc);
    }

    /// <summary>Check a token against the situation NOW. On success the token is spent.</summary>
    public ConfirmationCheck Validate(string? token, string operation, string documentKey, string requestHash,
                                      string? planFingerprintNow)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(token))
                return new(ConfirmationState.Missing,
                    "dry_run=false needs the confirmation_token returned by a dry run of exactly this request. " +
                    "Run it with dry_run=true first, read the plan, then apply with that token. Nothing was changed.");
            if (!_issued.TryGetValue(token!, out var e))
                return new(ConfirmationState.Unknown,
                    "No such confirmation token. Tokens are single-use, expire after 10 minutes and do not survive a " +
                    "Civil 3D restart. Re-run the dry run. Nothing was changed.");
            if (e.Used)
                return new(ConfirmationState.AlreadyUsed,
                    "That confirmation was already used. One approval authorises one execution. Re-run the dry run to " +
                    "see the CURRENT plan. Nothing was changed.");
            if (_now() > e.ExpiresUtc)
                return new(ConfirmationState.Expired,
                    "That confirmation expired (issued " + e.IssuedUtc.ToString("u") + "). The drawing may have moved " +
                    "since. Re-run the dry run. Nothing was changed.");
            if (!string.Equals(e.Operation, operation, StringComparison.Ordinal))
                return new(ConfirmationState.WrongOperation,
                    "That confirmation was issued for '" + e.Operation + "', not '" + operation + "'. Nothing was changed.");
            if (!string.Equals(e.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
                return new(ConfirmationState.DocumentChanged,
                    "That confirmation was issued for a DIFFERENT drawing than the active one. Nothing was changed. " +
                    "Activate the drawing you mean and re-run the dry run.");
            if (!string.Equals(e.RequestHash, requestHash, StringComparison.Ordinal))
                return new(ConfirmationState.RequestChanged,
                    "THIS REQUEST IS NOT THE ONE THAT WAS REHEARSED: at least one field differs from the dry run. " +
                    "Nothing was changed. Re-run the dry run with exactly the request you intend to apply.");
            if (e.PlanFingerprint != null && planFingerprintNow != null &&
                !string.Equals(e.PlanFingerprint, planFingerprintNow, StringComparison.Ordinal))
                return new(ConfirmationState.StalePlan,
                    "THE DRAWING MOVED AFTER THE DRY RUN: the request is identical but the drawing or the objects it " +
                    "resolves to are not what was rehearsed (a user edit or undo, or Civil 3D updating dependent " +
                    "objects in the background right after a previous write). Nothing was changed. Re-run the dry " +
                    "run and approve the current plan.");
            e.Used = true;
            return new(ConfirmationState.Valid, null);
        }
    }

    public int OutstandingCount { get { lock (_lock) { Prune(); return _issued.Count; } } }

    private void Prune()
    {
        var now = _now();
        foreach (var k in _issued.Where(kv => kv.Value.Used || now > kv.Value.ExpiresUtc).Select(kv => kv.Key).ToList())
            _issued.Remove(k);
    }
}
