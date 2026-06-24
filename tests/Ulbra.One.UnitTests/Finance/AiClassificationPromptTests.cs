using Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

namespace Ulbra.One.UnitTests.Finance;

public class AiClassificationPromptTests
{
    [Fact]
    public void Build_includes_payable_context_and_both_account_lists()
    {
        var prompt = AiClassificationPrompt.Build(
            description: "Cloud hosting subscription",
            counterpartyName: "Acme Cloud Ltda",
            documentNumber: "NF-123",
            totalAmount: 1234.56m,
            accountingAccounts: new[] { ("3.1.1", "Software expense") },
            financialAccounts: new[] { ("2.1.1", "Suppliers payable") });

        Assert.Contains("Cloud hosting subscription", prompt.User);
        Assert.Contains("Acme Cloud Ltda", prompt.User);
        Assert.Contains("NF-123", prompt.User);
        Assert.Contains("1234.56", prompt.User);
        Assert.Contains("3.1.1", prompt.User);
        Assert.Contains("2.1.1", prompt.User);
        Assert.False(string.IsNullOrWhiteSpace(prompt.System));
    }

    [Fact]
    public void Build_includes_fiscal_codes_when_present()
    {
        var prompt = AiClassificationPrompt.Build("desc", "Acme", "NF-1", 100m,
            new[] { ("3.1.1", "Software") }, new[] { ("2.1.1", "Suppliers") },
            cfops: ["5102"], ncms: ["49019900"]);
        Assert.Contains("5102", prompt.User);
        Assert.Contains("49019900", prompt.User);
    }
}
