using Microsoft.AspNetCore.Http;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Domain;

namespace Ulbra.One.Common.Application.Results;

public static class ResultExtensions
{
    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        // Failure and Problem are unexpected/internal conditions → 500 by design. Domain code
        // should prefer the specific types above for client-facing errors; a Failure reaching
        // here intentionally surfaces as a server error rather than leaking as a 4xx.
        _ => StatusCodes.Status500InternalServerError,
    };

    /// <summary>
    /// Routes the railway's <see cref="Error"/> through the same factories the imperative handlers use, so
    /// both styles produce byte-identical envelopes — <c>trace_id</c> and <c>error_code</c> included. The
    /// error's <c>Code</c> is the machine value; <c>title</c> comes from the class, never from the code.
    /// </summary>
    private static IResult Problem(Error error) => error.Type switch
    {
        ErrorType.Validation => ErrorResults.InvalidRequest(error.Description, error.Code),
        ErrorType.NotFound => ErrorResults.NotFound(error.Description, error.Code),
        ErrorType.Conflict => ErrorResults.InvalidState(error.Description, error.Code),
        ErrorType.Unauthorized => ErrorResults.Unauthorized(error.Description, error.Code),
        ErrorType.Forbidden => ErrorResults.Forbidden(error.Description, error.Code),
        _ => ErrorResults.InternalError(error.Description, error.Code),
    };

    /// <summary>200 OK with the value on success; Problem Details on failure.</summary>
    public static IResult ToOk<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : Problem(result.Error);

    /// <summary>201 Created (with location) on success; Problem Details on failure.</summary>
    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? TypedResults.Created(location(result.Value), result.Value) : Problem(result.Error);

    /// <summary>200 OK (no body) on success; Problem Details on failure.</summary>
    public static IResult ToOk(this Result result) =>
        result.IsSuccess ? TypedResults.Ok() : Problem(result.Error);
}
