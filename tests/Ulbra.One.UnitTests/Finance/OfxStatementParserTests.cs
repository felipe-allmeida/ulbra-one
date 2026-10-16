using System.Text;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Infrastructure.Conciliation;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class OfxStatementParserTests
{
    // "Clean" invariant OFX (dot decimals, USASCII, blank line before <OFX>) — the format proven to work before.
    private const string CleanOfx = """
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

    // Real-Santander shape: CHARSET:1252, NO blank line before <OFX>, unclosed SGML leaf tags,
    // comma decimals, a dot-thousands+comma value, and bracketed-timezone single-day period.
    private const string SantanderOfx = """
OFXHEADER:100
DATA:OFXSGML
VERSION:102
SECURITY:NONE
ENCODING:USASCII
CHARSET:1252
COMPRESSION:NONE
OLDFILEUID:NONE
NEWFILEUID:NONE
<OFX>
    <SIGNONMSGSRSV1>
        <SONRS>
            <STATUS><CODE>0<SEVERITY>INFO</STATUS>
            <DTSERVER>20260615120151[-3:GMT]
            <LANGUAGE>ENG
            <FI><ORG>SANTANDER<FID>SANTANDER</FI>
        </SONRS>
    </SIGNONMSGSRSV1>
    <BANKMSGSRSV1>
        <STMTTRNRS>
            <TRNUID>1
            <STATUS><CODE>0<SEVERITY>INFO</STATUS>
            <STMTRS>
                <CURDEF>BRL
                <BANKACCTFROM>
                    <BANKID>033
                    <ACCTID>3578130509766
                    <ACCTTYPE>CHECKING
                </BANKACCTFROM>
                <BANKTRANLIST>
                    <DTSTART>20260612000000[-3:GMT]
                    <DTEND>20260612000000[-3:GMT]
                    <STMTTRN>
                        <TRNTYPE>DEBIT
                        <DTPOSTED>20260612000000[-3:GMT]
                        <TRNAMT>-200,00
                        <FITID>F1
                        <CHECKNUM>493627
                        <PAYEEID>0
                        <MEMO>TARIFA PAGFOR COMPROVANTE LOTE
                    </STMTTRN>
                    <STMTTRN>
                        <TRNTYPE>CREDIT
                        <DTPOSTED>20260612000000[-3:GMT]
                        <TRNAMT>2650.000,00
                        <FITID>F2
                        <CHECKNUM>18404534
                        <PAYEEID>0
                        <MEMO>PIX RECEBIDO
                    </STMTTRN>
                    <STMTTRN>
                        <TRNTYPE>DEBIT
                        <DTPOSTED>20260612000000[-3:GMT]
                        <TRNAMT>-1975,87
                        <FITID>F3
                        <CHECKNUM>030612
                        <PAYEEID>0
                        <MEMO>PAGFOR PIX OUTRA INST- DIFEREN TIT
                    </STMTTRN>
                </BANKTRANLIST>
                <LEDGERBAL>
                    <BALAMT>586577,87
                    <DTASOF>20260615120151[-3:GMT]
                </LEDGERBAL>
            </STMTRS>
        </STMTTRNRS>
    </BANKMSGSRSV1>
</OFX>
""";

    private static async Task<ParsedStatement> ParseAsync(byte[] bytes)
    {
        var parser = new OfxStatementParser();
        Assert.Equal(StatementSource.OfxUpload, parser.Source);
        using var ms = new MemoryStream(bytes);
        return await parser.ParseAsync(ms, default);
    }

    private static Task<ParsedStatement> ParseUtf8(string ofx) => ParseAsync(Encoding.UTF8.GetBytes(ofx));

    [Fact]
    public async Task Parses_clean_invariant_ofx()
    {
        var st = await ParseUtf8(CleanOfx);

        Assert.Equal(2, st.Transactions.Count);
        var debit = st.Transactions[0];
        Assert.Equal("TXN-1", debit.ExternalId);
        Assert.Equal(new DateOnly(2026, 7, 10), debit.Date);
        Assert.Equal(-1000.00m, debit.Amount);
        Assert.Contains("CEEE", debit.Description);
        Assert.Equal("TXN-2", st.Transactions[1].ExternalId);
        Assert.Equal(2500.00m, st.Transactions[1].Amount);
    }

    [Fact]
    public async Task Parses_santander_ptBR_amounts_including_big_value()
    {
        var st = await ParseUtf8(SantanderOfx);

        Assert.Equal(3, st.Transactions.Count);
        Assert.Equal(-200.00m, st.Transactions[0].Amount);      // comma decimal
        Assert.Equal(2650000.00m, st.Transactions[1].Amount);   // dot thousands + comma decimal
        Assert.Equal(-1975.87m, st.Transactions[2].Amount);
        Assert.Contains("TARIFA", st.Transactions[0].Description);
    }

    [Fact]
    public async Task Reads_single_day_period_from_bracketed_dates()
    {
        var st = await ParseUtf8(SantanderOfx);

        Assert.Equal(new DateOnly(2026, 6, 12), st.PeriodStart);
        Assert.Equal(new DateOnly(2026, 6, 12), st.PeriodEnd);
        Assert.All(st.Transactions, t => Assert.Equal(new DateOnly(2026, 6, 12), t.Date));
    }

    [Fact]
    public async Task Reads_account_from_bankacctfrom()
    {
        var st = await ParseUtf8(SantanderOfx);

        Assert.NotNull(st.Account);
        Assert.Equal("033", st.Account!.BankId);
        Assert.Equal("3578130509766", st.Account.AccountId);
        Assert.Equal("CHECKING", st.Account.AccountType);
    }

    [Fact]
    public async Task Decodes_latin1_accented_memo()
    {
        // Build the bytes in Windows-1252/Latin1 (É = 0xC9); a UTF-8 reader would mojibake it.
        var ofx = SantanderOfx.Replace("TARIFA PAGFOR COMPROVANTE LOTE", "PAGAMENTO JOSÉ");
        var st = await ParseAsync(Encoding.Latin1.GetBytes(ofx));

        Assert.Contains("JOSÉ", st.Transactions[0].Description);
    }
}
