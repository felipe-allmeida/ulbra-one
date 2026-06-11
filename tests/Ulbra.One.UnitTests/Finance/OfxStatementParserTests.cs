using System.Text;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Infrastructure.Conciliation;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class OfxStatementParserTests
{
    private const string Ofx = """
OFXHEADER:100
DATA:OFXSGML
VERSION:102
SECURITY:NONE
ENCODING:USASCII

<OFX>
<SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20260731<LANGUAGE>POR<FI><ORG>BANCO<FID>033</FI></SONRS></SIGNONMSGSRSV1>
<BANKMSGSRSV1><STMTTRNRS><TRNUID>0<STATUS><CODE>0<SEVERITY>INFO</STATUS><STMTRS>
<CURDEF>BRL
<BANKACCTFROM><BANKID>033<ACCTID>12345<ACCTTYPE>CHECKING</BANKACCTFROM>
<BANKTRANLIST>
<DTSTART>20260701
<DTEND>20260731
<STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260710<TRNAMT>-1000.00<FITID>TXN-1<MEMO>PAG FORNEC CEEE</STMTTRN>
<STMTTRN><TRNTYPE>CREDIT<DTPOSTED>20260712<TRNAMT>2500.00<FITID>TXN-2<MEMO>DEPOSITO</STMTTRN>
</BANKTRANLIST>
<LEDGERBAL><BALAMT>1500.00<DTASOF>20260731</LEDGERBAL>
</STMTRS></STMTTRNRS></BANKMSGSRSV1>
</OFX>
""";

    [Fact]
    public async Task Parses_transactions_with_fitid_date_amount_memo()
    {
        var parser = new OfxStatementParser();
        Assert.Equal(StatementSource.OfxUpload, parser.Source);
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(Ofx));
        var st = await parser.ParseAsync(ms, default);

        Assert.Equal(2, st.Transactions.Count);
        var debit = st.Transactions[0];
        Assert.Equal("TXN-1", debit.ExternalId);
        Assert.Equal(new DateOnly(2026, 7, 10), debit.Date);
        Assert.Equal(-1000.00m, debit.Amount);
        Assert.Contains("CEEE", debit.Description);
        Assert.Equal("TXN-2", st.Transactions[1].ExternalId);
        Assert.Equal(2500.00m, st.Transactions[1].Amount);
    }
}
