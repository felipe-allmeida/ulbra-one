using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Remittances.CreateRemittance;

public sealed class CreateRemittance : IEndpoint
{
    public sealed record Request(BankAccountId PayingBankAccountId, DateOnly? DueCutoff);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("", Handle)
            .WithName("CreateRemittance").WithTags("Finance").RequireAuthorization()
            .Produces<CreateRemittanceResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

    private static async Task<IResult> Handle(
        Request req,
        UserContext user,
        IUnitOfWork uow,
        IRemittanceGenerationService service,
        TimeProvider clock,
        CancellationToken ct)
    {
        // Resolve companyId from the paying bank account (which owns the company association)
        var bankAccount = await uow.ReadSet<BankAccount>()
            .FirstOrDefaultAsync(b => b.Id == req.PayingBankAccountId.Value, ct);
        if (bankAccount is null)
            return TypedResults.Problem(detail: "Bank account not found.", statusCode: 404, title: "BankAccount.NotFound");

        var companyId = bankAccount.CompanyId;
        var generatedBy = user.UserId;
        var dueCutoff = req.DueCutoff ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        var result = await service.GenerateAsync(companyId, req.PayingBankAccountId.Value, dueCutoff, generatedBy, ct);

        if (result.IsFailure)
        {
            return result.Error.Code switch
            {
                "Remittance.NothingToRemit" => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
                "Remittance.BankNotSupported" => TypedResults.Problem(detail: result.Error.Description, statusCode: 422, title: result.Error.Code),
                "Remittance.MissingAgreementCode" => TypedResults.Problem(detail: result.Error.Description, statusCode: 422, title: result.Error.Code),
                _ => TypedResults.Problem(detail: result.Error.Description, statusCode: 400, title: result.Error.Code),
            };
        }

        var outcome = result.Value;
        var body = new CreateRemittanceResponse(
            RemittanceResponse.From(outcome.Remittance),
            outcome.Skipped.Select(SkippedInstallmentResponse.From).ToList());

        return TypedResults.Created($"/api/remittances/{new RemittanceId(outcome.Remittance.Id)}", body);
    }
}
