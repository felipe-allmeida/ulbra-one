using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Ulbra.One.SourceGenerators;
using Xunit;

namespace Ulbra.One.SourceGenerators.Tests;

public sealed class PrefixedIdGeneratorTests
{
    private const string StubSource = """
        namespace Ulbra.One.Common.Application.Ids
        {
            public interface IPrefixedId { }

            public class PrefixedIdJsonConverter<T> : System.Text.Json.Serialization.JsonConverter<T>
            {
                public override T Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options) => default!;
                public override void Write(System.Text.Json.Utf8JsonWriter writer, T value, System.Text.Json.JsonSerializerOptions options) { }
            }
        }
        """;

    private const string IdSource = """
        using Ulbra.One.Common.Application.Ids;
        namespace Ulbra.One.Common.Application.Ids
        {
            [PrefixedId("et_")]
            public readonly partial record struct EmptyId(System.Guid Value);
        }
        """;

    private static (ImmutableArray<Diagnostic> Diagnostics, ImmutableArray<GeneratedSourceResult> Sources) RunGenerator()
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [
                CSharpSyntaxTree.ParseText(StubSource),
                CSharpSyntaxTree.ParseText(IdSource)
            ],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PrefixedIdGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        return (result.Diagnostics, result.Results[0].GeneratedSources);
    }

    [Fact]
    public void Generator_EmitsAttributeSource()
    {
        var (_, sources) = RunGenerator();

        var attributeSource = sources.FirstOrDefault(s => s.HintName == "PrefixedIdAttribute.g.cs");
        Assert.False(attributeSource.Equals(default), "PrefixedIdAttribute.g.cs source was not produced");
        Assert.Contains("PrefixedIdAttribute", attributeSource.SourceText.ToString());
    }

    [Fact]
    public void Generator_EmitsEmptyIdSource_WithExpectedPrefix()
    {
        var (_, sources) = RunGenerator();

        var idSource = sources.FirstOrDefault(s => s.HintName == "EmptyId.g.cs");
        Assert.False(idSource.Equals(default), "EmptyId.g.cs source was not produced");

        var text = idSource.SourceText.ToString();
        Assert.Contains("Prefix => \"et_\"", text);
    }

    [Fact]
    public void Generator_EmitsNewMethod()
    {
        var (_, sources) = RunGenerator();

        var idSource = sources.First(s => s.HintName == "EmptyId.g.cs");
        Assert.Contains("public static EmptyId New()", idSource.SourceText.ToString());
    }

    [Fact]
    public void Generator_EmitsToStringWithPrefixAndValue()
    {
        var (_, sources) = RunGenerator();

        var idSource = sources.First(s => s.HintName == "EmptyId.g.cs");
        Assert.Contains("Prefix + Value.ToString(\"N\")", idSource.SourceText.ToString());
    }

    [Fact]
    public void Generator_EmitsTryParse()
    {
        var (_, sources) = RunGenerator();

        var idSource = sources.First(s => s.HintName == "EmptyId.g.cs");
        Assert.Contains("public static bool TryParse", idSource.SourceText.ToString());
    }

    [Fact]
    public void Generator_EmitsIParsableInterface()
    {
        var (_, sources) = RunGenerator();

        var idSource = sources.First(s => s.HintName == "EmptyId.g.cs");
        Assert.Contains("IParsable<EmptyId>", idSource.SourceText.ToString());
    }

    [Fact]
    public void Generator_ProducesNoDiagnosticErrors()
    {
        var (diagnostics, _) = RunGenerator();

        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }
}
