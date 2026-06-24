using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.UnitTests.Ids;

public class PrefixedIdRoundTripTests
{
    // ── PayableId ──────────────────────────────────────────────────────────────

    [Fact]
    public void PayableId_ToString_StartsWithPrefix_And_EndsWithNGuid()
    {
        var guid = Guid.NewGuid();
        var id = new PayableId(guid);
        Assert.StartsWith("payable_", id.ToString());
        Assert.EndsWith(guid.ToString("N"), id.ToString());
    }

    [Fact]
    public void PayableId_Parse_RoundTrips()
    {
        var id = new PayableId(Guid.NewGuid());
        Assert.Equal(id, PayableId.Parse(id.ToString(), null));
    }

    [Fact]
    public void PayableId_TryParse_AcceptsBareGuid()
    {
        var guid = Guid.NewGuid();
        Assert.True(PayableId.TryParse(guid.ToString("N"), null, out var bare));
        Assert.Equal(new PayableId(guid), bare);
    }

    [Fact]
    public void PayableId_New_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, PayableId.New().Value);
    }

    // ── BankTransactionId (prefix bank_txn_) ──────────────────────────────────

    [Fact]
    public void BankTransactionId_ToString_StartsWithPrefix_And_EndsWithNGuid()
    {
        var guid = Guid.NewGuid();
        var id = new BankTransactionId(guid);
        Assert.StartsWith("bank_txn_", id.ToString());
        Assert.EndsWith(guid.ToString("N"), id.ToString());
    }

    [Fact]
    public void BankTransactionId_Parse_RoundTrips()
    {
        var id = new BankTransactionId(Guid.NewGuid());
        Assert.Equal(id, BankTransactionId.Parse(id.ToString(), null));
    }

    [Fact]
    public void BankTransactionId_TryParse_AcceptsBareGuid()
    {
        var guid = Guid.NewGuid();
        Assert.True(BankTransactionId.TryParse(guid.ToString("N"), null, out var bare));
        Assert.Equal(new BankTransactionId(guid), bare);
    }

    [Fact]
    public void BankTransactionId_New_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, BankTransactionId.New().Value);
    }

    // ── AuditLogId ────────────────────────────────────────────────────────────

    [Fact]
    public void AuditLogId_ToString_StartsWithPrefix_And_EndsWithNGuid()
    {
        var guid = Guid.NewGuid();
        var id = new AuditLogId(guid);
        Assert.StartsWith("audit_log_", id.ToString());
        Assert.EndsWith(guid.ToString("N"), id.ToString());
    }

    [Fact]
    public void AuditLogId_Parse_RoundTrips()
    {
        var id = new AuditLogId(Guid.NewGuid());
        Assert.Equal(id, AuditLogId.Parse(id.ToString(), null));
    }

    [Fact]
    public void AuditLogId_TryParse_AcceptsBareGuid()
    {
        var guid = Guid.NewGuid();
        Assert.True(AuditLogId.TryParse(guid.ToString("N"), null, out var bare));
        Assert.Equal(new AuditLogId(guid), bare);
    }

    [Fact]
    public void AuditLogId_New_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, AuditLogId.New().Value);
    }

    // ── PayableAttachmentId (prefix attachment_) ───────────────────────────────

    [Fact]
    public void PayableAttachmentId_ToString_StartsWithPrefix_And_EndsWithNGuid()
    {
        var guid = Guid.NewGuid();
        var id = new PayableAttachmentId(guid);
        Assert.StartsWith("attachment_", id.ToString());
        Assert.EndsWith(guid.ToString("N"), id.ToString());
    }

    [Fact]
    public void PayableAttachmentId_Parse_RoundTrips()
    {
        var id = new PayableAttachmentId(Guid.NewGuid());
        Assert.Equal(id, PayableAttachmentId.Parse(id.ToString(), null));
    }

    [Fact]
    public void PayableAttachmentId_TryParse_AcceptsBareGuid()
    {
        var guid = Guid.NewGuid();
        Assert.True(PayableAttachmentId.TryParse(guid.ToString("N"), null, out var bare));
        Assert.Equal(new PayableAttachmentId(guid), bare);
    }

    [Fact]
    public void PayableAttachmentId_New_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, PayableAttachmentId.New().Value);
    }

    // ── StatementImportId (prefix statement_import_) ──────────────────────────

    [Fact]
    public void StatementImportId_ToString_StartsWithPrefix_And_EndsWithNGuid()
    {
        var guid = Guid.NewGuid();
        var id = new StatementImportId(guid);
        Assert.StartsWith("statement_import_", id.ToString());
        Assert.EndsWith(guid.ToString("N"), id.ToString());
    }

    [Fact]
    public void StatementImportId_Parse_RoundTrips()
    {
        var id = new StatementImportId(Guid.NewGuid());
        Assert.Equal(id, StatementImportId.Parse(id.ToString(), null));
    }

    [Fact]
    public void StatementImportId_TryParse_AcceptsBareGuid()
    {
        var guid = Guid.NewGuid();
        Assert.True(StatementImportId.TryParse(guid.ToString("N"), null, out var bare));
        Assert.Equal(new StatementImportId(guid), bare);
    }

    [Fact]
    public void StatementImportId_New_IsNotEmpty()
    {
        Assert.NotEqual(Guid.Empty, StatementImportId.New().Value);
    }
}
