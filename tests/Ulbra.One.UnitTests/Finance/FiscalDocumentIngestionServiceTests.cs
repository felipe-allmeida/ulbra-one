using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>The shared ingestion service (endpoint + requisition-approval consumer) — parse matrix, the
/// Service/Product type precedence, and the empty-extraction gate — all without a broker or a database.</summary>
public class FiscalDocumentIngestionServiceTests
{
    private static Payable NewPayable() =>
        new(Guid.NewGuid(), PayableOrigin.Purchase, Guid.NewGuid(), "Fornecedor",
            documentNumber: null, issueDate: new DateOnly(2026, 7, 1), description: "Compra",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(100m, 1, new DateOnly(2026, 7, 10), 1));

    private static FiscalDocumentSource Source() => new("key/doc.xml", "doc.xml", "application/xml", 512, Guid.NewGuid());

    private static ParsedFiscalDocument Parsed(FiscalDocumentFormat format, string? documentNumber = "NF-1", decimal? total = 100m,
        PayableType? suggested = null, DateOnly? issueDate = null) =>
        new(format, "acc-key", documentNumber, issueDate ?? new DateOnly(2026, 7, 1), total, "12345678000199", "Issuer", "99887766000155", "{}", [], SuggestedType: suggested);

    private static ParsedFiscalDocument Empty(FiscalDocumentFormat format) =>
        new(format, null, null, null, null, null, null, null, "{}", []);

    // ── stubs ──
    private sealed class StubStorage(Stream? blob) : IFileStorageService
    {
        public bool GetCalled { get; private set; }
        public Task<Stream?> GetAsync(string storagePath, CancellationToken ct = default) { GetCalled = true; return Task.FromResult(blob); }
        public Task<string> SaveAsync(string fileName, Stream content, CancellationToken ct = default) => Task.FromResult("new-key");
        public Task<string> SaveAsync(string fileName, Stream content, string keyPrefix, CancellationToken ct = default) => Task.FromResult($"{keyPrefix}new-key");
        public Task DeleteAsync(string storagePath, CancellationToken ct = default) => Task.CompletedTask;
        public Task<UploadTarget> CreateUploadUrl(string fileName, TimeSpan ttl, string? keyPrefix = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubReader(ParsedFiscalDocument? result, bool throws = false) : IFiscalDocumentReader
    {
        public Task<ParsedFiscalDocument?> ReadAsync(string fileName, Stream content, CancellationToken ct)
        {
            if (throws) throw new InvalidOperationException("malformed");
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingUow : IUnitOfWork
    {
        public List<object> Added { get; } = [];
        public IQueryable<T> ReadSet<T>() where T : class => Array.Empty<T>().AsQueryable();
        public IQueryable<T> Set<T>() where T : class => Array.Empty<T>().AsQueryable();
        public Task AddAsync<T>(T entity, CancellationToken ct) where T : class { Added.Add(entity); return Task.CompletedTask; }
        public void Remove<T>(T entity) where T : class { }
        public void RemoveRange<T>(IEnumerable<T> entities) where T : class { }
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(0);
        public void ClearChangeTracker() { }
    }

    private static FiscalDocumentIngestionService Service(IFileStorageService storage, IFiscalDocumentReader reader, CapturingUow uow) =>
        new(storage, reader, uow, TimeProvider.System);

    private static StubStorage StorageWithBlob() => new(new MemoryStream([1, 2, 3]));

    // ── tests ──

    [Fact]
    public async Task Xml_nfe_creates_document_and_sets_product_type_authoritatively()
    {
        var payable = NewPayable();
        var uow = new CapturingUow();
        var sut = Service(StorageWithBlob(), new StubReader(Parsed(FiscalDocumentFormat.NfeXml)), uow);

        var result = await sut.IngestAsync(payable, Source(), preParsed: null, rejectEmptyExtraction: false, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(FiscalDocumentFormat.NfeXml, result.Value.Format);
        Assert.Equal(payable.Id, result.Value.PayableId);
        Assert.Equal(PayableType.Product, payable.Type); // NF-e = Product, authoritative
        Assert.Contains(uow.Added, e => e is FiscalDocument);
    }

    [Fact]
    public async Task Xml_nfse_overrides_a_prior_service_choice_stays_service()
    {
        var payable = NewPayable();
        payable.SetType(PayableType.Product); // a prior (wrong) guess
        var sut = Service(StorageWithBlob(), new StubReader(Parsed(FiscalDocumentFormat.NfseXml)), new CapturingUow());

        var result = await sut.IngestAsync(payable, Source(), preParsed: null, rejectEmptyExtraction: false, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(PayableType.Service, payable.Type); // NFS-e = Service, authoritative over the prior value
    }

    [Fact]
    public async Task Pdf_only_fills_type_when_undetermined_never_overrides()
    {
        var payable = NewPayable();
        payable.SetType(PayableType.Service);
        // PDF suggests Product, but the payable already has a (human/requisition) type → keep it.
        var sut = Service(StorageWithBlob(), new StubReader(Parsed(FiscalDocumentFormat.Pdf, suggested: PayableType.Product)), new CapturingUow());

        var result = await sut.IngestAsync(payable, Source(), preParsed: null, rejectEmptyExtraction: false, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(PayableType.Service, payable.Type); // PDF guess doesn't override
    }

    [Fact]
    public async Task Pdf_fills_type_when_payable_has_none()
    {
        var payable = NewPayable(); // Type null
        var sut = Service(StorageWithBlob(), new StubReader(Parsed(FiscalDocumentFormat.Pdf, suggested: PayableType.Product)), new CapturingUow());

        var result = await sut.IngestAsync(payable, Source(), preParsed: null, rejectEmptyExtraction: false, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(PayableType.Product, payable.Type);
    }

    [Fact]
    public async Task Nota_fiscal_emission_date_becomes_the_payable_issue_date()
    {
        var payable = NewPayable(); // default IssueDate = 2026-07-01 (from the requisition's competency)
        var nfEmission = new DateOnly(2026, 7, 18);
        var sut = Service(StorageWithBlob(), new StubReader(Parsed(FiscalDocumentFormat.NfeXml, issueDate: nfEmission)), new CapturingUow());

        var result = await sut.IngestAsync(payable, Source(), preParsed: null, rejectEmptyExtraction: false, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(nfEmission, payable.IssueDate); // the NF's own emission date overrides the default data de emissão
    }

    [Fact]
    public async Task Parsed_document_number_propagates_to_payable_when_empty()
    {
        var payable = NewPayable(); // DocumentNumber null (requisition-sourced)
        var sut = Service(StorageWithBlob(), new StubReader(Parsed(FiscalDocumentFormat.NfeXml, documentNumber: "NF-777")), new CapturingUow());

        var result = await sut.IngestAsync(payable, Source(), preParsed: null, rejectEmptyExtraction: false, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("NF-777", payable.DocumentNumber); // the parsed número lands on the payable, not only the FiscalDocument
    }

    [Fact]
    public async Task Existing_document_number_is_not_overwritten()
    {
        var payable = NewPayable();
        payable.SetDocumentNumber("MANUAL-1"); // already filled (manual/earlier)
        var sut = Service(StorageWithBlob(), new StubReader(Parsed(FiscalDocumentFormat.NfeXml, documentNumber: "NF-777")), new CapturingUow());

        var result = await sut.IngestAsync(payable, Source(), preParsed: null, rejectEmptyExtraction: false, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("MANUAL-1", payable.DocumentNumber); // fill only when empty — never clobber
    }

    [Fact]
    public async Task Missing_blob_returns_not_found()
    {
        var sut = Service(new StubStorage(null), new StubReader(Parsed(FiscalDocumentFormat.NfeXml)), new CapturingUow());
        var result = await sut.IngestAsync(NewPayable(), Source(), preParsed: null, rejectEmptyExtraction: false, default);
        Assert.True(result.IsFailure);
        Assert.Equal("fiscal_document.not_found", result.Error.Code);
    }

    [Fact]
    public async Task Malformed_content_returns_parse_failed_without_throwing()
    {
        var sut = Service(StorageWithBlob(), new StubReader(null, throws: true), new CapturingUow());
        // Must NOT propagate — a malformed marked attachment must degrade, not fault the consumer.
        var result = await sut.IngestAsync(NewPayable(), Source(), preParsed: null, rejectEmptyExtraction: false, default);
        Assert.True(result.IsFailure);
        Assert.Equal("fiscal_document.parse_failed", result.Error.Code);
    }

    [Fact]
    public async Task Unsupported_format_returns_unsupported()
    {
        var sut = Service(StorageWithBlob(), new StubReader(null), new CapturingUow());
        var result = await sut.IngestAsync(NewPayable(), Source(), preParsed: null, rejectEmptyExtraction: false, default);
        Assert.True(result.IsFailure);
        Assert.Equal("fiscal_document.unsupported_format", result.Error.Code);
    }

    [Fact]
    public async Task Empty_extraction_rejected_when_flag_set()
    {
        var sut = Service(StorageWithBlob(), new StubReader(Empty(FiscalDocumentFormat.Pdf)), new CapturingUow());
        var result = await sut.IngestAsync(NewPayable(), Source(), preParsed: null, rejectEmptyExtraction: true, default);
        Assert.True(result.IsFailure);
        Assert.Equal("fiscal_document.empty_extraction", result.Error.Code);
    }

    [Fact]
    public async Task Empty_extraction_allowed_when_flag_false()
    {
        var sut = Service(StorageWithBlob(), new StubReader(Empty(FiscalDocumentFormat.Pdf)), new CapturingUow());
        var result = await sut.IngestAsync(NewPayable(), Source(), preParsed: null, rejectEmptyExtraction: false, default);
        Assert.True(result.IsSuccess); // manual flow persists the confirmed (empty) preview
    }

    [Fact]
    public async Task PreParsed_skips_the_blob_read()
    {
        var storage = new StubStorage(null); // would fail with NotFound if read
        var sut = Service(storage, new StubReader(null, throws: true), new CapturingUow());

        var result = await sut.IngestAsync(NewPayable(), Source(), preParsed: Parsed(FiscalDocumentFormat.NfeXml), rejectEmptyExtraction: true, default);

        Assert.True(result.IsSuccess);
        Assert.False(storage.GetCalled); // pre-parsed → no storage read, no re-parse
    }
}
