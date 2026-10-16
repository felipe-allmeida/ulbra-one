using System.Reflection;
using System.Text.RegularExpressions;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.UnitTests.Common;

/// <summary>
/// Guards the two properties the error envelope depends on: every code is well-formed, and every code the
/// API can emit has a pt-BR message on the other side of the wire.
/// <para>
/// The second one is the whole reason the catalogue exists as constants. Before it, codes were literals
/// scattered across handlers and the frontend keyed its translations off free-form titles — nothing could
/// enumerate either side, so 114 codes silently showed the user English. A test can only catch that drift
/// if the catalogue is enumerable.
/// </para>
/// </summary>
public class ErrorCatalogueTests
{
    private static readonly Regex CodeShape = new("^[a-z][a-z0-9_]*\\.[a-z][a-z0-9_]*$", RegexOptions.Compiled);

    public static TheoryData<string, string> AllCodes()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, value) in Catalogue()) data.Add(name, value);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Every_code_is_lower_snake_case_aggregate_dot_reason(string name, string value)
    {
        Assert.Matches(CodeShape, value);
        // The constant's own name must match its value, so `ErrorCodes.Payable.NotFound` cannot quietly
        // point at some other aggregate's code after a copy-paste.
        Assert.Equal(name, ToPascalPath(value));
    }

    [Fact]
    public void Every_code_is_unique()
    {
        var duplicates = Catalogue()
            .GroupBy(c => c.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(x => x.Name))}")
            .ToList();

        Assert.True(duplicates.Count == 0, $"Codes emitted by more than one constant:\n{string.Join("\n", duplicates)}");
    }

    [Fact]
    public void Every_code_has_a_pt_BR_message()
    {
        var translated = TranslatedCodes();
        var untranslated = Catalogue().Select(c => c.Value).Where(v => !translated.Contains(v)).Order().ToList();

        Assert.True(untranslated.Count == 0,
            "These error codes would reach the user in English — add them under `errors.codes` in "
            + $"web/src/i18n/locales/pt-BR.ts:\n{string.Join("\n", untranslated)}");
    }

    [Fact]
    public void No_pt_BR_message_is_orphaned()
    {
        var known = Catalogue().Select(c => c.Value).ToHashSet();
        var orphans = TranslatedCodes().Where(c => !known.Contains(c)).Order().ToList();

        Assert.True(orphans.Count == 0,
            "These translations no longer match any code the API emits — drop them from "
            + $"web/src/i18n/locales/pt-BR.ts:\n{string.Join("\n", orphans)}");
    }


    /// <summary>
    /// The catalogue only guards codes that reach it. A handler that writes the code inline —
    /// <c>Error.NotFound("Company.NotFound", …)</c> — bypasses every other test here: it never lands in
    /// <see cref="ErrorCodes"/>, so nothing checks its format and nothing notices it has no translation.
    /// It then goes out on the wire in the old PascalCase shape while the frontend looks up snake_case.
    /// This exact drift came back with three endpoints in a single merge.
    /// </summary>
    [Fact]
    public void No_error_code_is_written_as_a_literal()
    {
        // A string literal sitting in a code position: first argument of an Error factory, or the
        // argument right after the detail in an ErrorResults factory.
        var codeLiteral = new Regex(
            """(Error\.\w+\(\s*"[^"]+")|(ErrorResults\.\w+\([^;]*?,\s*"[A-Za-z][^"]*"\s*[,)])""",
            RegexOptions.Compiled);

        var offenders = SourceFiles()
            .SelectMany(file => codeLiteral.Matches(File.ReadAllText(file))
                .Select(m => $"{Path.GetFileName(file)}: {m.Value.ReplaceLineEndings(" ")}"))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These write the error code inline instead of referencing ErrorCodes, so it escapes the "
            + $"catalogue and the translation guard:\n{string.Join("\n", offenders)}");
    }

    /// <summary>Every C# file under <c>src/</c> that could hold an error code.</summary>
    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(RepoFile("src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                && !f.EndsWith("ErrorCodes.cs", StringComparison.Ordinal));

    /// <summary>Every <c>public const string</c> under <see cref="ErrorCodes"/>, as (Aggregate.Reason, value).</summary>
    private static IEnumerable<(string Name, string Value)> Catalogue() =>
        typeof(ErrorCodes)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(aggregate => aggregate
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f is { IsLiteral: true, FieldType: var t } && t == typeof(string))
                .Select(f => ($"{aggregate.Name}.{f.Name}", (string)f.GetRawConstantValue()!)));

    /// <summary>The keys under <c>errors.codes</c> in the frontend's pt-BR bundle.</summary>
    private static HashSet<string> TranslatedCodes()
    {
        var body = File.ReadAllText(RepoFile("web", "src", "i18n", "locales", "pt-BR.ts"));
        var block = body.Split("codes: {", 2)[1].Split("\n    },", 2)[0];
        return Regex.Matches(block, @"'([a-z][a-z0-9_.]*)':").Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static string RepoFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ulbra.One.slnx"))) dir = dir.Parent;
        Assert.True(dir is not null, "Could not locate the repository root from the test output directory.");
        return Path.Combine([dir!.FullName, .. parts]);
    }

    /// <summary><c>payable.not_found</c> → <c>Payable.NotFound</c>.</summary>
    private static string ToPascalPath(string code) =>
        string.Join('.', code.Split('.').Select(segment =>
            string.Concat(segment.Split('_').Select(word => char.ToUpperInvariant(word[0]) + word[1..]))));
}
