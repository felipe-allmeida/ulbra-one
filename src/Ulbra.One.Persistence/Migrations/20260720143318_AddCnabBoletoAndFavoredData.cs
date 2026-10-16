using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCnabBoletoAndFavoredData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "boleto_digitable_line",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "due_date",
                schema: "finance",
                table: "remittance_lines",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "favored_address_cep",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "favored_address_city",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "favored_address_complement",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "favored_address_neighborhood",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "favored_address_number",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "favored_address_state",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "favored_address_street",
                schema: "finance",
                table: "remittance_lines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "boleto_digitable_line",
                schema: "finance",
                table: "installments",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "address_cep",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "address_city",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "address_complement",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "address_number",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "address_state",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "address_street",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "agency_digit",
                schema: "finance",
                table: "bank_accounts",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "boleto_digitable_line",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "due_date",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "favored_address_cep",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "favored_address_city",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "favored_address_complement",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "favored_address_neighborhood",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "favored_address_number",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "favored_address_state",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "favored_address_street",
                schema: "finance",
                table: "remittance_lines");

            migrationBuilder.DropColumn(
                name: "boleto_digitable_line",
                schema: "finance",
                table: "installments");

            migrationBuilder.DropColumn(
                name: "address_cep",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "address_city",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "address_complement",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "address_number",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "address_state",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "address_street",
                schema: "finance",
                table: "bank_accounts");

            migrationBuilder.DropColumn(
                name: "agency_digit",
                schema: "finance",
                table: "bank_accounts");
        }
    }
}
