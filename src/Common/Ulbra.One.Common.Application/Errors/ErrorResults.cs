using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Ulbra.One.Common.Application.Errors;

/// <summary>
/// The single way an endpoint returns an error. Every factory here goes through one builder, so
/// <c>trace_id</c> and <c>error_code</c> are structurally impossible to forget — that, rather than saving
/// keystrokes, is why these exist.
/// <para>
/// The split between the two identity fields matters: <c>title</c> is the human summary RFC 9457 asks for
/// and comes from the error class, while <c>error_code</c> is the stable machine value the frontend keys its
/// pt-BR messages off. Handlers pass the code from <c>ErrorCodes</c>; they never phrase the title.
/// </para>
/// </summary>
public static class ErrorResults
{
    public const string TraceIdKey = "trace_id";
    public const string ErrorCodeKey = "error_code";

    /// <summary>The request is malformed or references something invalid, with no single field to blame.</summary>
    /// <remarks>For per-field failures use <see cref="ValidationError(IDictionary{string, string[]}, string)"/>.</remarks>
    public static ProblemHttpResult InvalidRequest(string detail, string errorCode) =>
        Build(StatusCodes.Status400BadRequest, "Invalid request", detail, errorCode);

    /// <summary>The operation is not legal for the resource's current state (a paid payable being edited).</summary>
    public static ProblemHttpResult InvalidState(string detail, string errorCode, string? instance = null) =>
        Build(StatusCodes.Status409Conflict, "Invalid state", detail, errorCode, instance);

    /// <summary>
    /// The request is well-formed but cannot be carried out as asked — the remittance generator meeting a
    /// bank it has no layout for, or a payer whose registration data is incomplete.
    /// </summary>
    public static ProblemHttpResult UnprocessableEntity(string detail, string errorCode) =>
        Build(StatusCodes.Status422UnprocessableEntity, "Unprocessable entity", detail, errorCode);

    /// <summary>The resource does not exist, or the caller may not know that it does.</summary>
    public static ProblemHttpResult NotFound(string detail, string errorCode) =>
        Build(StatusCodes.Status404NotFound, "Resource not found", detail, errorCode);

    /// <summary>Authenticated, but not allowed to perform this action.</summary>
    public static ProblemHttpResult Forbidden(string detail, string errorCode) =>
        Build(StatusCodes.Status403Forbidden, "Forbidden", detail, errorCode);

    /// <summary>Authentication is missing or no longer valid.</summary>
    public static ProblemHttpResult Unauthorized(string detail, string errorCode) =>
        Build(StatusCodes.Status401Unauthorized, "Unauthorized", detail, errorCode);

    /// <summary>The feature exists but is not configured in this environment (Google sign-in without a client id).</summary>
    public static ProblemHttpResult NotImplemented(string detail, string errorCode) =>
        Build(StatusCodes.Status501NotImplemented, "Not implemented", detail, errorCode);

    /// <summary>A dependency is temporarily down and the caller should retry or fall back (the CNPJ lookup).</summary>
    public static ProblemHttpResult ServiceUnavailable(string detail, string errorCode) =>
        Build(StatusCodes.Status503ServiceUnavailable, "Service unavailable", detail, errorCode);

    /// <summary>The caller exceeded the rate limit.</summary>
    public static ProblemHttpResult TooManyRequests(string detail, string errorCode, string? instance = null) =>
        Build(StatusCodes.Status429TooManyRequests, "Too many requests", detail, errorCode, instance);

    /// <summary>An external dependency the operation needs failed (an insurer, a CNPJ lookup, a bank).</summary>
    public static ProblemHttpResult ThirdPartyError(string detail, string errorCode) =>
        Build(StatusCodes.Status502BadGateway, "Third party error", detail, errorCode);

    /// <summary>Unexpected failure. Reserved for the global handler — endpoints should not reach for it.</summary>
    public static ProblemHttpResult InternalError(string detail, string errorCode, string? instance = null) =>
        Build(StatusCodes.Status500InternalServerError, "An error occurred while processing your request",
            detail, errorCode, instance);

    /// <summary>
    /// One or more fields of the request are invalid, keyed by field name.
    /// </summary>
    /// <remarks>
    /// Still 400 today. Moving field errors to 422 — and separating them from the state errors that share
    /// this status — is deliberately left to the validation phase, so this change alters the error BODY
    /// without moving any status code.
    /// </remarks>
    public static ValidationProblem ValidationError(IDictionary<string, string[]> errors, string errorCode) =>
        TypedResults.ValidationProblem(
            errors,
            title: "Invalid request",
            extensions: Extensions(errorCode));

    /// <summary>Convenience for a single offending field.</summary>
    public static ValidationProblem ValidationError(string field, string message, string errorCode) =>
        ValidationError(new Dictionary<string, string[]> { [field] = [message] }, errorCode);

    private static ProblemHttpResult Build(int status, string title, string detail, string errorCode,
        string? instance = null) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            instance: instance,
            extensions: Extensions(errorCode));

    private static Dictionary<string, object?> Extensions(string errorCode) => new()
    {
        [TraceIdKey] = CurrentTraceId(),
        [ErrorCodeKey] = errorCode,
    };

    /// <summary>
    /// The active trace id, so a user reporting "it failed" can be tied to the exact request in telemetry.
    /// Falls back to the all-zero id when nothing is tracing (unit tests, a request outside an activity)
    /// rather than omitting the field — a caller can always read it, it just will not correlate.
    /// </summary>
    public static string CurrentTraceId() =>
        Activity.Current?.TraceId.ToHexString() ?? new string('0', 32);
}
