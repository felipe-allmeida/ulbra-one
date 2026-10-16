using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using Ulbra.One.Finance.Application.Contracts;

namespace Ulbra.One.Finance.Application.Reports;

/// <summary>Header metadata for the report PDF (who generated it and when).</summary>
public sealed record PaymentsReportPdfHeader(string GeneratedBy, DateTimeOffset GeneratedAt);

/// <summary>
/// Renders the "Relação de Pagamentos" PDF with MigraDoc, reproducing the legacy layout: a per-page header
/// (Gerado por / Data-Hora / Página X de N), a RESUMO section grouped by unit (with per-unit + grand totals),
/// then a DETALHAMENTO section (Unidade → Fornecedor → parcelas, with subtotals and a grand total). Pure — no
/// EF/IO — so it stays unit-testable; the caller loads and enriches the rows.
/// </summary>
public static class PaymentsReportPdf
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color Brand = new(0x00, 0x4B, 0x30);
    private static readonly Color BrandSoft = new(0xF2, 0xF7, 0xF4);
    private static readonly Color Slate300 = new(203, 213, 225);
    private static readonly Color Slate400 = new(148, 163, 184);
    private static readonly Color Slate500 = new(100, 116, 139);
    private static readonly Color Slate600 = new(71, 85, 105);
    private static readonly Color Slate800 = new(30, 41, 59);

    // Column widths (mm) mirroring the legacy layout; sum = 186mm (A4 minus 12mm margins each side).
    private static readonly double[] ColumnWidths = [32, 42, 36, 36, 28, 12];

    private sealed record ProviderGroup(string Name, string? Document, decimal Total, List<PaymentReportItemResponse> Rows);
    private sealed record UnitGroup(string Name, decimal Total, List<ProviderGroup> Providers);

    public static byte[] Build(
        IReadOnlyList<PaymentReportItemResponse> rows, PaymentsReportPdfHeader header, bool showDescription,
        string webBaseUrl)
    {
        PdfFontResolver.EnsureRegistered();

        var baseUrl = webBaseUrl.TrimEnd('/');
        var units = GroupByUnit(rows);
        var grandTotal = rows.Sum(r => r.Amount);

        var doc = new Document();
        var normal = doc.Styles["Normal"]!;
        normal.Font.Name = PdfFontResolver.FamilyName;
        normal.Font.Size = 8;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        // The page header (logo + "Gerado por") lives in the top-margin band. HeaderDistance places it near the
        // top edge; TopMargin must clear the whole header (logo ~10mm + 3 text lines + its bottom rule) plus a
        // gap, otherwise the body starts inside the header and its rule cuts across "RESUMO DE PAGAMENTOS".
        section.PageSetup.HeaderDistance = Unit.FromMillimeter(12);
        section.PageSetup.TopMargin = Unit.FromMillimeter(34);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(15);
        section.PageSetup.LeftMargin = Unit.FromMillimeter(12);
        section.PageSetup.RightMargin = Unit.FromMillimeter(12);

        BuildPageHeader(section, header);
        BuildResumo(section, units, grandTotal);
        section.AddPageBreak();
        BuildDetalhamento(section, units, grandTotal, showDescription, baseUrl);

        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    private static List<UnitGroup> GroupByUnit(IReadOnlyList<PaymentReportItemResponse> rows) =>
        rows.GroupBy(r => r.UnitName ?? "Externo")
            .Select(u => new UnitGroup(
                u.Key,
                u.Sum(r => r.Amount),
                u.GroupBy(r => (r.CounterpartyName, r.VendorDocument))
                    .Select(p => new ProviderGroup(
                        p.Key.CounterpartyName, p.Key.VendorDocument, p.Sum(r => r.Amount),
                        p.OrderByDescending(r => r.Amount).ToList()))
                    .OrderByDescending(p => p.Total).ToList()))
            .OrderByDescending(u => u.Total).ToList();

    private static void BuildPageHeader(Section section, PaymentsReportPdfHeader header)
    {
        // Logo on the left, generation info + page number on the right — laid out with a 2-column table
        // so the two sit side by side (MigraDoc headers otherwise stack vertically), matching the legacy.
        var table = section.Headers.Primary.AddTable();
        table.Borders.Width = 0;
        table.AddColumn(Unit.FromMillimeter(42));
        table.AddColumn(Unit.FromMillimeter(144));
        var row = table.AddRow();
        row.Borders.Bottom.Width = 0.3;
        row.Borders.Bottom.Color = Slate300;
        row.BottomPadding = Unit.FromMillimeter(2);
        row.Cells[1].VerticalAlignment = VerticalAlignment.Center;

        var logo = LogoImageSource();
        if (logo is not null)
        {
            var img = row.Cells[0].AddImage(logo);
            img.Width = Unit.FromMillimeter(34);
            img.LockAspectRatio = true;
        }

        var page = row.Cells[1].AddParagraph();
        page.Format.Alignment = ParagraphAlignment.Right;
        page.Format.Font.Size = 8;
        page.Format.Font.Color = Slate600;
        page.AddText("Página ");
        page.AddPageField();
        page.AddText(" de ");
        page.AddNumPagesField();

        var gen = row.Cells[1].AddParagraph();
        gen.Format.Font.Size = 8;
        gen.Format.Font.Color = Slate600;
        gen.AddText($"Gerado por: {header.GeneratedBy}");
        gen.AddLineBreak();
        gen.AddText($"Data/Hora de Geração: {header.GeneratedAt.ToString("dd/MM/yyyy HH:mm:ss", Br)}");
    }

    /// <summary>The institutional logo as a MigraDoc "base64:" image source, or null if not embedded.</summary>
    private static string? LogoImageSource()
    {
        using var stream = typeof(PaymentsReportPdf).Assembly.GetManifestResourceStream("logo.png");
        if (stream is null) return null;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return "base64:" + Convert.ToBase64String(ms.ToArray());
    }

    private static void BuildResumo(Section section, List<UnitGroup> units, decimal grandTotal)
    {
        SectionTitle(section, "RESUMO DE PAGAMENTOS");

        // Two-column grid of unit cards (like the legacy). Units are already sorted by total desc; each row
        // holds two cards with a thin gutter column between them.
        var grid = section.AddTable();
        grid.Borders.Width = 0;
        grid.AddColumn(Unit.FromMillimeter(91));
        grid.AddColumn(Unit.FromMillimeter(4));
        grid.AddColumn(Unit.FromMillimeter(91));
        for (var i = 0; i < units.Count; i += 2)
        {
            var row = grid.AddRow();
            row.Format.SpaceAfter = Unit.FromMillimeter(2);
            row.Cells[0].VerticalAlignment = VerticalAlignment.Top;
            row.Cells[2].VerticalAlignment = VerticalAlignment.Top;
            RenderUnitCard(row.Cells[0], units[i]);
            if (i + 1 < units.Count) RenderUnitCard(row.Cells[2], units[i + 1]);
        }

        var total = section.AddParagraph();
        total.Format.SpaceBefore = Unit.FromMillimeter(3);
        total.Format.Shading.Color = BrandSoft;
        total.Format.Font.Bold = true;
        total.Format.Font.Size = 8;
        total.Format.Font.Color = Brand;
        total.Format.AddTabStop(Unit.FromMillimeter(186), TabAlignment.Right);
        total.AddText("TOTAL");
        total.AddTab();
        total.AddText(Money(grandTotal));
    }

    private static void RenderUnitCard(Cell cell, UnitGroup unit)
    {
        cell.Shading.Color = new Color(252, 253, 252);
        cell.Borders.Width = 0.2;
        cell.Borders.Color = Slate300;

        var head = cell.AddParagraph();
        head.Format.Font.Bold = true;
        head.Format.Font.Size = 7;
        head.Format.Font.Color = Brand;
        head.Format.Borders.Bottom.Width = 0.2;
        head.Format.Borders.Bottom.Color = Slate300;
        head.Format.SpaceBefore = Unit.FromMillimeter(1.5);
        head.Format.SpaceAfter = Unit.FromMillimeter(1);
        head.Format.AddTabStop(Unit.FromMillimeter(87), TabAlignment.Right);
        head.AddText(unit.Name.ToUpper(Br));
        head.AddTab();
        head.AddText(Money(unit.Total));

        foreach (var provider in unit.Providers)
        {
            var line = cell.AddParagraph();
            line.Format.Font.Size = 6;
            line.Format.Font.Color = Slate600;
            line.Format.AddTabStop(Unit.FromMillimeter(87), TabAlignment.Right);
            var tag = line.AddFormattedText($"• [{ResumoTag(provider.Rows)}] ", TextFormat.Bold);
            tag.Color = Slate500;
            line.AddText(provider.Name);
            line.AddTab();
            var value = line.AddFormattedText(Money(provider.Total), TextFormat.Bold);
            value.Color = Brand;
        }
    }

    private static void BuildDetalhamento(
        Section section, List<UnitGroup> units, decimal grandTotal, bool showDescription, string baseUrl)
    {
        SectionTitle(section, "DETALHAMENTO DOS PAGAMENTOS");

        var table = section.AddTable();
        table.Borders.Width = 0;
        foreach (var w in ColumnWidths) table.AddColumn(Unit.FromMillimeter(w));

        AddColumnHeader(table);

        foreach (var unit in units)
        {
            // Unit banner: the strongest visual divider (the "divisão entre unidades/empresas"). Bigger + a
            // spacer above so each unit block reads as its own section, mirroring the legacy report.
            AddBannerRow(table, unit.Name.ToUpper(Br), $"Total da Unidade: {Money(unit.Total)}", Brand, Colors.White, 10.5, spaceBefore: 3);

            foreach (var provider in unit.Providers)
            {
                var doc = provider.Document is { Length: > 0 } d ? $" - CNPJ/CPF: {d}" : "";
                AddBannerRow(table, provider.Name + doc, $"Total: {Money(provider.Total)}", null, Slate800, 8.5, spaceBefore: 1.2);

                foreach (var row in provider.Rows)
                {
                    AddDetailRow(table, row, baseUrl);
                    if (showDescription) AddDescriptionRow(table, row);
                }
            }
        }

        var grand = section.AddParagraph();
        grand.Format.SpaceBefore = Unit.FromMillimeter(3);
        grand.Format.Shading.Color = Brand;
        grand.Format.Font.Bold = true;
        grand.Format.Font.Size = 9;
        grand.Format.Font.Color = Colors.White;
        grand.Format.AddTabStop(Unit.FromMillimeter(186), TabAlignment.Right);
        grand.AddText("TOTAL DOS PAGAMENTOS RELACIONADOS");
        grand.AddTab();
        grand.AddText(Money(grandTotal));
    }

    private static void AddColumnHeader(Table table)
    {
        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Format.Font.Bold = true;
        head.Format.Font.Size = 6.5;
        head.Format.Font.Color = Brand;
        head.Borders.Bottom.Width = 0.5;
        head.Borders.Bottom.Color = Slate300;
        string[] titles = ["CÓD. / SOLICITANTE", "UNIDADE / NATUREZA", "APROVADOR / DATA", "VENCIMENTO / VALOR", "STATUS / SITUAÇÃO", "VER"];
        for (var i = 0; i < titles.Length; i++) head.Cells[i].AddParagraph(titles[i]);
    }

    private static void AddBannerRow(
        Table table, string left, string right, Color? shading, Color textColor, double size, double spaceBefore = 0)
    {
        // Blank spacer row above, for clear separation between blocks (units read as distinct sections).
        if (spaceBefore > 0)
        {
            var spacer = table.AddRow();
            spacer.HeightRule = RowHeightRule.Exactly;
            spacer.Height = Unit.FromMillimeter(spaceBefore);
            spacer.Cells[0].MergeRight = ColumnWidths.Length - 1;
        }

        var row = table.AddRow();
        if (shading is { } s) row.Shading.Color = s;
        row.Format.Font.Bold = true;
        row.Format.Font.Size = size;
        row.Format.Font.Color = textColor;
        row.VerticalAlignment = VerticalAlignment.Center;
        row.TopPadding = Unit.FromMillimeter(shading is null ? 1 : 1.8);
        row.BottomPadding = Unit.FromMillimeter(shading is null ? 1 : 1.8);
        if (shading is null)
        {
            row.Borders.Bottom.Width = 0.2;
            row.Borders.Bottom.Color = Slate300;
        }

        // Two real cells (name left / total right) instead of a tab stop in a merged cell — a right-aligned
        // paragraph in its own cell lands reliably at the table's right edge; the merged-cell tab did not.
        var name = row.Cells[0];
        name.MergeRight = 2; // cells 0..2
        name.AddParagraph(left);

        var total = row.Cells[3];
        total.MergeRight = 2; // cells 3..5
        var tp = total.AddParagraph(right);
        tp.Format.Alignment = ParagraphAlignment.Right;
    }

    private static void AddDetailRow(Table table, PaymentReportItemResponse row, string baseUrl)
    {
        var r = table.AddRow();
        r.TopPadding = Unit.FromMillimeter(0.8);
        r.BottomPadding = Unit.FromMillimeter(0.8);
        r.Borders.Bottom.Width = 0.15;
        r.Borders.Bottom.Color = new Color(241, 245, 249);

        TwoLineCell(r.Cells[0], $"#{CodeOrDash(row.RequisitionCode)}", Slate800, 7.5, true,
            Truncate(row.RequesterName ?? "Externo/Importado", 20), Slate400, 6.5, italic: true);
        TwoLineCell(r.Cells[1], Truncate(row.UnitName ?? "—", 22), Slate800, 7.5, true,
            Truncate(row.NatureName ?? "—", 24), Slate500, 6.5);
        TwoLineCell(r.Cells[2], Truncate(row.ApproverName ?? "-", 20), Slate800, 7.5, true,
            row.ApprovedAt is { } at ? at.ToString("dd/MM/yyyy", Br) : "-", Slate400, 6.5);
        TwoLineCell(r.Cells[3], Money(row.Amount), Brand, 7.5, true,
            $"Venc: {row.DueDate.ToString("dd/MM/yyyy", Br)}", Slate500, 6.5);
        TwoLineCell(r.Cells[4], row.StatusLabel.ToUpper(Br), Slate600, 5.5, true,
            row.PaymentTypeLabel, Slate500, 5.5);

        // "Ver" links back to the SPA payable detail (same target as the on-screen Ver button). The base URL
        // is resolved per environment by the caller, so the link is clickable in local/dev/prod.
        var ver = r.Cells[5].AddParagraph();
        var link = ver.AddHyperlink($"{baseUrl}/app/payables/{row.PayableId}", HyperlinkType.Web);
        var verText = link.AddFormattedText("Ver", TextFormat.Bold);
        verText.Color = Brand;
        verText.Size = 7.5;
        verText.Underline = Underline.Single;
    }

    private static void AddDescriptionRow(Table table, PaymentReportItemResponse row)
    {
        if (string.IsNullOrWhiteSpace(row.Description) && string.IsNullOrWhiteSpace(row.Observation)) return;
        var r = table.AddRow();
        var cell = r.Cells[0];
        cell.MergeRight = ColumnWidths.Length - 1;
        if (!string.IsNullOrWhiteSpace(row.Description))
        {
            var d = cell.AddParagraph($"Descrição: {row.Description.Trim()}");
            d.Format.Font.Size = 5.5;
            d.Format.Font.Italic = true;
            d.Format.Font.Color = Slate600;
        }
        if (!string.IsNullOrWhiteSpace(row.Observation))
        {
            var o = cell.AddParagraph($"Observação: {row.Observation.Trim()}");
            o.Format.Font.Size = 5.5;
            o.Format.Font.Italic = true;
            o.Format.Font.Color = Slate500;
        }
    }

    private static void TwoLineCell(
        Cell cell, string main, Color mainColor, double mainSize, bool mainBold,
        string sub, Color subColor, double subSize, bool italic = false)
    {
        var m = cell.AddParagraph();
        var mt = m.AddFormattedText(main, mainBold ? TextFormat.Bold : TextFormat.NotBold);
        mt.Color = mainColor;
        mt.Size = mainSize;
        var s = cell.AddParagraph();
        var st = s.AddFormattedText(sub, italic ? TextFormat.Italic : TextFormat.NotItalic);
        st.Color = subColor;
        st.Size = subSize;
    }

    private static void SectionTitle(Section section, string text)
    {
        var title = section.AddParagraph(text);
        title.Format.Font.Bold = true;
        title.Format.Font.Size = 11;
        title.Format.Font.Color = Brand;
        title.Format.SpaceAfter = Unit.FromMillimeter(1);
        title.Format.Borders.Bottom.Width = 0.4;
        title.Format.Borders.Bottom.Color = Brand;
    }

    /// <summary>Compact frequency tag for the RESUMO provider line, mirroring the legacy wording.</summary>
    private static string ResumoTag(List<PaymentReportItemResponse> rows)
    {
        if (rows.Count != 1) return $"{rows.Count} pgtos";
        var row = rows[0];
        return row.PaymentTypeLabel switch
        {
            "Conta fixa" => "Fixo",
            "Pgto. Único" => "Única",
            _ => row.PaymentTypeLabel.Replace("Pgto ", "Parc. "),
        };
    }

    private static string Money(decimal value) => value.ToString("C", Br);

    private static string CodeOrDash(int? code) => code?.ToString(Br) ?? "Externo";

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 2)] + "..";
}
