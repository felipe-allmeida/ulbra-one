using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries.CreateJournalEntry;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries.GetJournalEntry;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries.ListJournalEntries;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries.PostJournalEntry;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries.ReverseJournalEntry;
using Ulbra.One.Accounting.Application.Endpoints.JournalEntries.UpdateJournalEntry;
using Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.CreateLedgerAccount;
using Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.GetLedgerAccount;
using Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.ListLedgerAccounts;
using Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.SeedLedgerAccounts;
using Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.UpdateLedgerAccount;
using Ulbra.One.Accounting.Application.Endpoints.PendingPostings.GetPendingPostings;
using Ulbra.One.Accounting.Application.Endpoints.PendingPostings.ResolvePendingPosting;
using Ulbra.One.Accounting.Application.Endpoints.Periods.CloseAccountingPeriod;
using Ulbra.One.Accounting.Application.Endpoints.Periods.ListAccountingPeriods;
using Ulbra.One.Accounting.Application.Endpoints.Periods.ReopenAccountingPeriod;
using Ulbra.One.Accounting.Application.Endpoints.Reports.GetBalanceSheet;
using Ulbra.One.Accounting.Application.Endpoints.Reports.GetIncomeStatement;
using Ulbra.One.Accounting.Application.Endpoints.Reports.GetTrialBalance;
using Ulbra.One.Common.Application.Extensions;

namespace Ulbra.One.Accounting.Application;

public static class AccountingEndpoints
{
    public static IEndpointRouteBuilder MapAccountingEndpoints(this IEndpointRouteBuilder builder)
    {
        var ledgerAccounts = builder.MapGroup("/ledger-accounts");
        ledgerAccounts.MapEndpoint<ListLedgerAccounts>();
        ledgerAccounts.MapEndpoint<GetLedgerAccount>();
        ledgerAccounts.MapEndpoint<CreateLedgerAccount>();
        ledgerAccounts.MapEndpoint<UpdateLedgerAccount>();
        ledgerAccounts.MapEndpoint<SeedLedgerAccounts>();

        var journalEntries = builder.MapGroup("/journal-entries");
        journalEntries.MapEndpoint<ListJournalEntries>();
        journalEntries.MapEndpoint<GetJournalEntry>();
        journalEntries.MapEndpoint<CreateJournalEntry>();
        journalEntries.MapEndpoint<UpdateJournalEntry>();
        journalEntries.MapEndpoint<PostJournalEntry>();
        journalEntries.MapEndpoint<ReverseJournalEntry>();

        var reports = builder.MapGroup("/accounting");
        reports.MapEndpoint<GetTrialBalance>();
        reports.MapEndpoint<GetIncomeStatement>();
        reports.MapEndpoint<GetBalanceSheet>();

        var pendingPostings = builder.MapGroup("/accounting/pending-postings");
        pendingPostings.MapEndpoint<GetPendingPostings>();
        pendingPostings.MapEndpoint<ResolvePendingPosting>();

        var periods = builder.MapGroup("/accounting/periods");
        periods.MapEndpoint<ListAccountingPeriods>();
        periods.MapEndpoint<CloseAccountingPeriod>();
        periods.MapEndpoint<ReopenAccountingPeriod>();

        return builder;
    }
}
