// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - "cannot lie": how a write proves what it did.
// (Pattern from Horizun Revit MCP's Guard/Reconcile/ApplicationOutcome.)
//
// A write is reported as done only from a RE-READ of the drawing in a NEW
// transaction after the commit. Each property the caller asked for becomes one
// check {what, requested, actual, verified}. The overall status is:
//
//   match     every check verified
//   partial   some verified, some not        (an error, with per-object detail)
//   mismatch  none verified                  (an error, even if nothing threw)
//
// An empty set of checks is NEVER a pass: "we verified nothing" is not "it worked".
// Numbers compare with an explicit tolerance; NaN/Infinity are never comparable.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public sealed class VerificationSet
{
    private readonly JsonArray _checks = new();
    private int _verified;
    private int _failed;

    public int Count => _verified + _failed;
    public int Verified => _verified;
    public int Failed => _failed;

    /// <param name="failNote">Explains a FAILED check; never shown when the check passes.</param>
    public void Check(string what, JsonNode? requested, JsonNode? actual, bool verified, string? failNote = null)
    {
        var o = new JsonObject
        {
            ["what"] = what,
            ["requested"] = requested?.DeepClone(),
            ["actual"] = actual?.DeepClone(),
            ["verified"] = verified,
        };
        if (!verified) o["note"] = failNote ?? "MISMATCH: the drawing does not hold the requested value after commit.";
        _checks.Add(o);
        if (verified) _verified++; else _failed++;
    }

    /// <summary>
    /// Text equality. Case-insensitive by default because the host normalises the case of many names. A rename
    /// must pass ignoreCase: false: a case-only rename is otherwise verified whether or not it happened.
    /// </summary>
    public void Text(string what, string? requested, string? actual, bool ignoreCase = true) =>
        Check(what, requested, actual,
            requested != null && actual != null &&
            string.Equals(requested, actual, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

    public void Number(string what, double requested, double actual, double tolerance)
    {
        var comparable = Hz.IsFinite(requested) && Hz.IsFinite(actual);
        Check(what, Hz.Finite(requested), Hz.Finite(actual), comparable && Math.Abs(requested - actual) <= tolerance,
            comparable ? null : "UNMEASURED: a value is not a finite number; not comparable.");
    }

    public void Flag(string what, bool requested, bool actual) => Check(what, requested, actual, requested == actual);

    public string Status => Count == 0 ? "unverified" : _failed == 0 ? "match" : _verified == 0 ? "mismatch" : "partial";

    public bool AllVerified => Count > 0 && _failed == 0;

    public JsonObject ToJson(string how = "Re-read from the drawing in a new transaction after commit.") => new()
    {
        ["status"] = Status,
        ["checks_total"] = Count,
        ["checks_verified"] = _verified,
        ["checks_failed"] = _failed,
        ["checks"] = _checks.DeepClone(),
        ["how"] = how,
    };
}

public static class Reconcile
{
    /// <summary>Compare two measurements of the same quantity (e.g. Civil 3D volume vs grid sampling).</summary>
    public static JsonObject Compare(string what, double a, string aSource, double b, string bSource, double tolerancePct)
    {
        var comparable = Hz.IsFinite(a) && Hz.IsFinite(b);
        var diff = comparable ? a - b : double.NaN;
        var basis = Math.Max(Math.Abs(a), Math.Abs(b));
        var pct = comparable && basis > 0 ? Math.Abs(diff) / basis * 100.0 : (comparable ? 0.0 : double.NaN);
        return new JsonObject
        {
            ["what"] = what,
            [aSource] = Hz.Finite(a),
            [bSource] = Hz.Finite(b),
            ["comparable"] = comparable,
            ["difference"] = Hz.Finite(diff),
            ["difference_pct"] = Hz.Finite(pct, 4),
            ["agree"] = comparable && pct <= tolerancePct,
            ["tolerance_pct"] = tolerancePct,
        };
    }
}
