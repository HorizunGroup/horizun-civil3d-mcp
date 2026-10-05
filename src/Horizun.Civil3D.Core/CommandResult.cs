// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - the result of one plug-in command, and its error codes.
//
// A failure always carries a stable machine code (what to do next) and a
// sentence for a human (what happened). "Nothing ran" vs "something may have
// run" is stated in the message whenever it is not obvious.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class ErrorCodes
{
    public const string InvalidInput = "invalid_input";
    public const string NotFound = "not_found";
    public const string Ambiguous = "ambiguous";
    public const string Busy = "busy";
    public const string NoDocument = "no_document";
    public const string DocumentMismatch = "document_mismatch";
    public const string ReadOnlyDocument = "read_only_document";
    public const string NotEditable = "not_editable";
    public const string PermissionDenied = "permission_denied";
    public const string ConfirmationRequired = "confirmation_required";
    public const string ConfirmationRefused = "confirmation_refused";
    public const string Unsupported = "unsupported";
    public const string VerificationFailed = "verification_failed";
    public const string TransactionFailed = "transaction_failed";
    public const string QueueFull = "queue_full";
    public const string Timeout = "timeout";
    public const string NotResponding = "not_responding";
    public const string Internal = "internal_error";
    public const string Transport = "transport_failed";
    public const string ContractMismatch = "contract_mismatch";
    public const string NoInstance = "no_civil3d_instance";
}

public sealed class CommandResult
{
    public bool Success { get; private init; }
    public JsonObject? Data { get; private init; }
    public string? Error { get; private init; }
    public string? Code { get; private init; }
    public JsonObject? Detail { get; private init; }

    public static CommandResult Ok(JsonObject data) => new() { Success = true, Data = data };

    public static CommandResult Fail(string code, string message, JsonObject? detail = null) =>
        new() { Success = false, Code = code, Error = message, Detail = detail };
}

/// <summary>Raised inside a command to refuse with a code; the dispatcher turns it into a failure.</summary>
public sealed class HzRefusal : Exception
{
    public string Code { get; }
    public JsonObject? Detail { get; }

    public HzRefusal(string code, string message, JsonObject? detail = null) : base(message)
    {
        Code = code;
        Detail = detail;
    }
}
