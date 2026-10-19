using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayableCnabRemittance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cnab_agreement_code",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "remittances",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paying_bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    file_storage_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    generated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remittances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "remittance_lines",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    remittance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    favored_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    favored_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    favored_bank_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    favored_agency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    favored_account = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    favored_account_digit = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    favored_pix_key = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_remittance_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_remittance_lines_remittances_remittance_id",
                        column: x => x.remittance_id,
                        principalSchema: "finance",
                        principalTable: "remittances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_remittance_lines_remittance_id",
                schema: "finance",
                table: "remittance_lines",
                column: "remittance_id");

            migrationBuilder.CreateIndex(
                name: "ix_remittances_company_id_paying_bank_account_id_sequence_numb",
                schema: "finance",
                table: "remittances",
                columns: new[] { "company_id", "paying_bank_account_id", "sequence_number" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "remittance_lines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "remittances",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "cnab_agreement_code",
                schema: "finance",
                table: "bank_accounts");
        }
    }
}
