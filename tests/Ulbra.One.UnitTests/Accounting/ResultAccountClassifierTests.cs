using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Application.Seeding;

namespace Ulbra.One.UnitTests.Accounting;

public class ResultAccountClassifierTests
{
    [Theory]
    [InlineData("4.01.01.02", "RECEITA DE DOAÇÕES", AccountGroup.Revenue)]
    [InlineData("4.01", "RECEITAS", AccountGroup.Revenue)]
    [InlineData("4.01.09.01.01", "Receita de Aluguéis", AccountGroup.Revenue)]
    [InlineData("4.02.01", "DESPESAS COM RECURSOS HUMANOS", AccountGroup.Expense)]
    [InlineData("4.02.02.01.05", "Despesas de Consumo (Água/Esgoto/Energia Elétrica/Comunicação)", AccountGroup.Expense)]
    [InlineData("3.02", "PROVISÃO PARA CSLL E IRPJ", AccountGroup.Expense)]
    [InlineData("4", "SUPERÁVIT/DÉFICIT LÍQUIDO DO EXERCÍCIO", AccountGroup.Expense)]
    public void Classifies_result_accounts(string code, string name, AccountGroup expected)
    {
        Assert.Equal(expected, ResultAccountClassifier.Classify(code, name));
    }
}
