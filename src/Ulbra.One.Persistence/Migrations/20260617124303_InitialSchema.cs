using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ulbra.One.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "registry");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "bank_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    agency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    account_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    account_digit = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    account_type = table.Column<int>(type: "integer", nullable: false),
                    alias = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    opening_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bank_transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    match_status = table.Column<int>(type: "integer", nullable: false),
                    suggested_installment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    matched_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bank_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chart_accounts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chart_type = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    nature = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chart_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "classification_policies",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    require_to_approve = table.Column<bool>(type: "boolean", nullable: false),
                    require_to_pay = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classification_policies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cnpj = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address_cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    address_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_complement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address_neighborhood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hire_date = table.Column<DateOnly>(type: "date", nullable: false),
                    address_cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    address_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_complement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address_neighborhood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expense_categories",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payables",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counterparty_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    approved_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    rejected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accounting_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    financial_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "receivables",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<int>(type: "integer", nullable: false),
                    payer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: true),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revenue_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ledger_account_id = table.Column<Guid>(type: "uuid", nullable: true),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cancelled_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    write_off_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivables", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "statement_imports",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    transaction_count = table.Column<int>(type: "integer", nullable: false),
                    new_count = table.Column<int>(type: "integer", nullable: false),
                    duplicate_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_statement_imports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    counterparty_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    google_subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                schema: "registry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    document = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address_cep = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    address_street = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    address_complement = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address_neighborhood = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    payment_terms = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bank_info = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_centers",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    annual_budget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    auto_approval_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_centers", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_centers_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "core",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "units",
                schema: "core",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    acronym = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_units_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "core",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "installments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installments", x => x.id);
                    table.ForeignKey(
                        name: "fk_installments_payables_payable_id",
                        column: x => x.payable_id,
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payable_attachments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    uploaded_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payable_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payable_attachments_payables_payable_id",
                        column: x => x.payable_id,
                        principalSchema: "finance",
                        principalTable: "payables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receivable_installments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    received_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receivable_installments", x => x.id);
                    table.ForeignKey(
                        name: "fk_receivable_installments_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cost_center_units",
                schema: "core",
                columns: table => new
                {
                    cost_center_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_center_units", x => new { x.cost_center_id, x.unit_id });
                    table.ForeignKey(
                        name: "fk_cost_center_units_cost_centers_cost_center_id",
                        column: x => x.cost_center_id,
                        principalSchema: "core",
                        principalTable: "cost_centers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_cost_center_units_units_unit_id",
                        column: x => x.unit_id,
                        principalSchema: "core",
                        principalTable: "units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fine = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    reversal_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    proof_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    proof_content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    proof_storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    proof_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_installments_installment_id",
                        column: x => x.installment_id,
                        principalSchema: "finance",
                        principalTable: "installments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    installment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receivable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_date = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    bank_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    interest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    fine = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_reversed = table.Column<bool>(type: "boolean", nullable: false),
                    reversal_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    proof_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    proof_content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    proof_storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    proof_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_receipts_receivable_installments_installment_id",
                        column: x => x.installment_id,
                        principalSchema: "finance",
                        principalTable: "receivable_installments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_receipts_receivables_receivable_id",
                        column: x => x.receivable_id,
                        principalSchema: "finance",
                        principalTable: "receivables",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_company_id",
                schema: "finance",
                table: "bank_accounts",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_bank_accounts_company_id_bank_code_agency_account_number_ac",
                schema: "finance",
                table: "bank_accounts",
                columns: new[] { "company_id", "bank_code", "agency", "account_number", "account_digit" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_bank_account_id_external_id",
                schema: "finance",
                table: "bank_transactions",
                columns: new[] { "bank_account_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_company_id_match_status",
                schema: "finance",
                table: "bank_transactions",
                columns: new[] { "company_id", "match_status" });

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_date",
                schema: "finance",
                table: "bank_transactions",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_bank_transactions_import_id",
                schema: "finance",
                table: "bank_transactions",
                column: "import_id");

            migrationBuilder.CreateIndex(
                name: "ix_chart_accounts_chart_type_code",
                schema: "finance",
                table: "chart_accounts",
                columns: new[] { "chart_type", "code" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_classification_policies_company_id",
                schema: "finance",
                table: "classification_policies",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_cnpj",
                schema: "core",
                table: "companies",
                column: "cnpj",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_center_units_unit_id",
                schema: "core",
                table: "cost_center_units",
                column: "unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_centers_company_id_code",
                schema: "core",
                table: "cost_centers",
                columns: new[] { "company_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_company_id_document",
                schema: "registry",
                table: "customers",
                columns: new[] { "company_id", "document" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_employees_company_id_document",
                schema: "registry",
                table: "employees",
                columns: new[] { "company_id", "document" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_expense_categories_name",
                schema: "finance",
                table: "expense_categories",
                column: "name",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_installments_due_date",
                schema: "finance",
                table: "installments",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_installments_payable_id",
                schema: "finance",
                table: "installments",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_installments_status",
                schema: "finance",
                table: "installments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payable_attachments_payable_id",
                schema: "finance",
                table: "payable_attachments",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_company_id",
                schema: "finance",
                table: "payables",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_cost_center_id",
                schema: "finance",
                table: "payables",
                column: "cost_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_payables_status",
                schema: "finance",
                table: "payables",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payables_vendor_id",
                schema: "finance",
                table: "payables",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_bank_account_id",
                schema: "finance",
                table: "payments",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_installment_id",
                schema: "finance",
                table: "payments",
                column: "installment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_payable_id",
                schema: "finance",
                table: "payments",
                column: "payable_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_bank_account_id",
                schema: "finance",
                table: "receipts",
                column: "bank_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_installment_id",
                schema: "finance",
                table: "receipts",
                column: "installment_id");

            migrationBuilder.CreateIndex(
                name: "ix_receipts_receivable_id",
                schema: "finance",
                table: "receipts",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_installments_due_date",
                schema: "finance",
                table: "receivable_installments",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_installments_receivable_id",
                schema: "finance",
                table: "receivable_installments",
                column: "receivable_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivable_installments_status",
                schema: "finance",
                table: "receivable_installments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_company_id",
                schema: "finance",
                table: "receivables",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_cost_center_id",
                schema: "finance",
                table: "receivables",
                column: "cost_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_payer_id",
                schema: "finance",
                table: "receivables",
                column: "payer_id");

            migrationBuilder.CreateIndex(
                name: "ix_receivables_status",
                schema: "finance",
                table: "receivables",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_statement_imports_company_id_bank_account_id",
                schema: "finance",
                table: "statement_imports",
                columns: new[] { "company_id", "bank_account_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_company_id",
                schema: "finance",
                table: "transactions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_cost_center_id",
                schema: "finance",
                table: "transactions",
                column: "cost_center_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_customer_id",
                schema: "finance",
                table: "transactions",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_type_status",
                schema: "finance",
                table: "transactions",
                columns: new[] { "type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_vendor_id",
                schema: "finance",
                table: "transactions",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_units_company_id_acronym",
                schema: "core",
                table: "units",
                columns: new[] { "company_id", "acronym" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                schema: "identity",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendors_company_id_document",
                schema: "registry",
                table: "vendors",
                columns: new[] { "company_id", "document" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bank_accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "bank_transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "chart_accounts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "classification_policies",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "cost_center_units",
                schema: "core");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "expense_categories",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payable_attachments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receipts",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "statement_imports",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "vendors",
                schema: "registry");

            migrationBuilder.DropTable(
                name: "cost_centers",
                schema: "core");

            migrationBuilder.DropTable(
                name: "units",
                schema: "core");

            migrationBuilder.DropTable(
                name: "installments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receivable_installments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "companies",
                schema: "core");

            migrationBuilder.DropTable(
                name: "payables",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "receivables",
                schema: "finance");
        }
    }
}
