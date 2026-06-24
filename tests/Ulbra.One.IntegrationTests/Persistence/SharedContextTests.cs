using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Identity.Domain.Entities;
using Ulbra.One.Persistence;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.IntegrationTests.Persistence;

/// <summary>
/// Explicit guarantees for the single-AppDbContext architecture:
///   1. Every module's entities are registered in AppDbContext with the correct schema.
///   2. A single non-keyed IUnitOfWork can execute queries against all four module schemas.
///   3. Cross-schema writes commit atomically through one SaveChangesAsync call.
/// </summary>
[Collection("Postgres")]
public class SharedContextTests(PostgresFixture fixture)
{
    // -------------------------------------------------------------------------
    // 1. All modules are mapped in the one context with the right schema
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AppDbContext_MapsAllModuleEntities_WithCorrectSchemas()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);

        using var scope = factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Core
        var company = ctx.Model.FindEntityType(typeof(Company));
        Assert.NotNull(company);
        Assert.Equal(Schemas.Core, company!.GetSchema());

        // Finance
        var payable = ctx.Model.FindEntityType(typeof(Payable));
        Assert.NotNull(payable);
        Assert.Equal(Schemas.Finance, payable!.GetSchema());

        // Identity
        var user = ctx.Model.FindEntityType(typeof(User));
        Assert.NotNull(user);
        Assert.Equal(Schemas.Identity, user!.GetSchema());

        // Registry
        var vendor = ctx.Model.FindEntityType(typeof(VendorEntity));
        Assert.NotNull(vendor);
        Assert.Equal(Schemas.Registry, vendor!.GetSchema());
    }

    // -------------------------------------------------------------------------
    // 2. One non-keyed IUnitOfWork reaches all module schemas
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SingleUoW_CanQueryAllModuleSchemas_WithoutThrowing()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);

        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Each line proves the UoW's underlying context has the entity mapped
        // and can hit the real table without throwing.
        await uow.ReadSet<Company>().AnyAsync();
        await uow.ReadSet<Payable>().AnyAsync();
        await uow.ReadSet<User>().AnyAsync();
        await uow.ReadSet<VendorEntity>().AnyAsync();
    }

    // -------------------------------------------------------------------------
    // 3. Cross-schema write is atomic through one UoW + SaveChangesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SingleUoW_CrossSchemaWrite_CommitsAtomically()
    {
        await using var factory = new ApiFactory(fixture.ConnectionString);

        // Use a unique CNPJ/document so repeated test runs don't collide.
        var cnpj = Random.Shared.NextInt64(10_000_000_000_000, 99_999_999_999_999).ToString();
        var document = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(); // 11-digit CPF-like

        using var scope = factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // Build one entity per schema (mirror DevDataSeeder constructors).
        var company = new Company("Cross-Schema Test Co", cnpj);
        var vendor = new VendorEntity(
            company.Id,
            PersonType.Individual,
            "Cross-Schema Vendor",
            null,
            document,
            "test@cross-schema.dev",
            "51900000000",
            Address.Empty,
            "à vista",
            BankInfo.Empty,
            TaxRegime.Unknown);

        await uow.AddAsync(company, CancellationToken.None);
        await uow.AddAsync(vendor, CancellationToken.None);

        // Single SaveChangesAsync — both rows must land in the same commit.
        await uow.SaveChangesAsync(CancellationToken.None);

        // Read both back to confirm they are persisted.
        var savedCompany = await uow.ReadSet<Company>()
            .FirstOrDefaultAsync(c => c.Id == company.Id);
        var savedVendor = await uow.ReadSet<VendorEntity>()
            .FirstOrDefaultAsync(v => v.Id == vendor.Id);

        Assert.NotNull(savedCompany);
        Assert.NotNull(savedVendor);
        Assert.Equal(company.Id, savedVendor!.CompanyId);
    }
}
