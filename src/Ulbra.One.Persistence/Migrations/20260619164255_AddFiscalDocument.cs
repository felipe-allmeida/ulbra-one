using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "kind",
                schema: "finance",
                table: "payables",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fiscal_documents",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    format = table.Column<int>(type: "integer", nullable: false),
                    access_key = table.Column<string>(type: "character varying(44)", maxLength: 44, nullable: true),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    issuer_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    issuer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recipient_document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    raw_extraction_json = table.Column<string>(type: "jsonb", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fiscal_documents", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_documents_payable_id",
                schema: "finance",
                table: "fiscal_documents",
                column: "payable_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_documents",
                schema: "finance");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "finance",
                table: "payables");
        }
    }
}
