using Microsoft.AspNetCore.Http;
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

    private static IResult Problem(Error error) => TypedResults.Problem(
        detail: error.Description,
        statusCode: ToStatusCode(error.Type),
        title: error.Code);

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
