using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Application.Results;

namespace Ulbra.One.UnitTests.Results;

public class ResultExtensionsTests
{
    [Fact]
    public void Success_Value_Maps_To_Ok()
    {
        Result<int> r = 42;
        var http = r.ToOk();
        Assert.IsType<Ok<int>>(http);
    }

    [Fact]
    public void NotFound_Error_Maps_To_404()
    {
        var r = Result.Failure<int>(Error.NotFound("X.NotFound", "missing"));
        var http = r.ToOk();
        var problem = Assert.IsType<ProblemHttpResult>(http);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
    }

    [Fact]
    public void Validation_Error_Maps_To_400()
    {
        var r = Result.Failure<int>(Error.Validation("X.Bad", "bad"));
        var problem = Assert.IsType<ProblemHttpResult>(r.ToOk());
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public void Conflict_Error_Maps_To_409()
    {
        var r = Result.Failure<int>(Error.Conflict("X.Dup", "dup"));
        var problem = Assert.IsType<ProblemHttpResult>(r.ToOk());
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }
}
