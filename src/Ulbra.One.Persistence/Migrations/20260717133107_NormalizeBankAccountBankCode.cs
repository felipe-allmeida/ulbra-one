using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeBankAccountBankCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: canonicalize existing bank_code to the 3-digit COMPE form ("33"/"0033" → "033"),
            // mirroring BankCodes.Normalize. Fixes legacy Santander accounts that were rejected as
            // "banco não suportado" by the strict == "033" comparison. Idempotent (IS DISTINCT FROM guard);
            // rows with no significant digits are left untouched (they are not a valid bank code either way).
            migrationBuilder.Sql(
                """
                UPDATE finance.bank_accounts
                SET bank_code = lpad(ltrim(regexp_replace(bank_code, '\D', '', 'g'), '0'), 3, '0')
                WHERE ltrim(regexp_replace(coalesce(bank_code, ''), '\D', '', 'g'), '0') <> ''
                  AND bank_code IS DISTINCT FROM lpad(ltrim(regexp_replace(bank_code, '\D', '', 'g'), '0'), 3, '0');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data normalization is irreversible — the pre-normalization values are not recoverable.
        }
    }
}
