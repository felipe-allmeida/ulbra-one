namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for BankStatementImport entities (wire form <c>statement_import_{guid:N}</c>).</summary>
[PrefixedId("statement_import_")]
public readonly partial record struct StatementImportId(Guid Value);
