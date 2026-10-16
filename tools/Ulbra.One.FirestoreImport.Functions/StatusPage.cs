using System.Net;
using System.Text;
using Ulbra.One.FirestoreImport;

namespace Ulbra.One.FirestoreImport.Functions;

/// <summary>Renders a background import job as a self-contained HTML page for browser viewing — the status
/// endpoint content-negotiates (HTML for a browser, JSON for curl). Auto-refreshes while the job runs.</summary>
internal static class StatusPage
{
    public static string Render(ImportJob job)
    {
        var (badge, label) = job.Status switch
        {
            ImportJobStatus.Completed => ("ok", "Concluído"),
            ImportJobStatus.Failed => ("fail", "Falhou"),
            _ => ("run", "Em andamento"),
        };

        var items = job.Result?.Items().ToList() ?? [];
        var rows = new StringBuilder();
        foreach (var i in items)
            rows.Append(Row(i.Entity, i.Read, i.Inserted, i.Updated, i.Skipped, i.DeadLettered, total: false));
        if (items.Count > 0)
            rows.Append(Row("total", items.Sum(i => i.Read), items.Sum(i => i.Inserted), items.Sum(i => i.Updated),
                items.Sum(i => i.Skipped), items.Sum(i => i.DeadLettered), total: true));

        var duration = job.FinishedAt is { } f ? $"{(f - job.StartedAt).TotalSeconds:0.0}s" : "—";
        var finished = job.FinishedAt is { } ff ? $"{ff:yyyy-MM-dd HH:mm:ss} UTC" : "—";
        var refresh = job.Status == ImportJobStatus.Running ? "<meta http-equiv=\"refresh\" content=\"2\">" : "";
        var dryRun = job.DryRun ? "<span class=\"tag dry\">dry-run</span>" : "<span class=\"tag apply\">apply</span>";
        var errorBlock = string.IsNullOrEmpty(job.Error)
            ? ""
            : $"<div class=\"err\"><strong>Erro:</strong> {WebUtility.HtmlEncode(job.Error)}</div>";
        var body = items.Count == 0
            ? "<p class=\"empty\">Sem resultado ainda — a importação está rodando. Esta página atualiza sozinha.</p>"
            : $"""
                <table><thead><tr>
                  <th>Item</th><th class="num">Lidos</th><th class="num">Inseridos</th>
                  <th class="num">Atualizados</th><th class="num">Ignorados</th><th class="num">Dead-letter</th>
                </tr></thead><tbody>{rows}</tbody></table>
                """;

        return $"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">{refresh}
            <title>Importação · {label}</title>
            <style>{Css}</style></head>
            <body><main>
              <h1>Importação Firestore → Ulbra One</h1>
              <div class="meta"><span class="badge {badge}">{label}</span>{dryRun}<code>{WebUtility.HtmlEncode(job.RunId)}</code></div>
              <dl>
                <div><dt>Início</dt><dd>{job.StartedAt:yyyy-MM-dd HH:mm:ss} UTC</dd></div>
                <div><dt>Fim</dt><dd>{finished}</dd></div>
                <div><dt>Duração</dt><dd>{duration}</dd></div>
              </dl>
              {errorBlock}
              {body}
            </main></body></html>
            """;
    }

    private static string Row(string label, int read, int ins, int upd, int skip, int dead, bool total)
    {
        var cls = total ? " class=\"total\"" : "";
        var deadCls = dead > 0 ? "num bad" : "num";
        return $"<tr{cls}><td>{WebUtility.HtmlEncode(label)}</td>"
             + $"<td class=\"num\">{read}</td><td class=\"num\">{ins}</td><td class=\"num\">{upd}</td>"
             + $"<td class=\"num\">{skip}</td><td class=\"{deadCls}\">{dead}</td></tr>";
    }

    private const string Css = """
        :root{color-scheme:light dark}*{box-sizing:border-box}
        body{margin:0;font:15px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif;background:#f6f7f9;color:#1a1d21}
        main{max-width:760px;margin:40px auto;padding:0 20px}
        h1{font-size:20px;margin:0 0 16px}
        .meta{display:flex;align-items:center;gap:10px;flex-wrap:wrap;margin-bottom:16px}
        .badge{padding:3px 10px;border-radius:999px;font-weight:600;font-size:13px}
        .badge.ok{background:#e3f6e9;color:#1a7f43}.badge.fail{background:#fde8e8;color:#c0392b}.badge.run{background:#fff4e0;color:#b5741a}
        .tag{padding:2px 8px;border-radius:6px;font-size:12px;font-weight:600}
        .tag.dry{background:#eef2ff;color:#4553b8}.tag.apply{background:#e8f2ee;color:#1a7f5a}
        code{font:13px ui-monospace,SFMono-Regular,Menlo,monospace;background:#eceef1;padding:2px 6px;border-radius:6px}
        dl{display:flex;gap:24px;flex-wrap:wrap;margin:0 0 20px;padding:12px 16px;background:#fff;border:1px solid #e4e7eb;border-radius:10px}
        dl div{display:flex;flex-direction:column;gap:2px}
        dt{font-size:12px;color:#6b7280;text-transform:uppercase;letter-spacing:.04em}
        dd{margin:0;font-weight:600}
        table{width:100%;border-collapse:collapse;background:#fff;border:1px solid #e4e7eb;border-radius:10px;overflow:hidden}
        th,td{padding:9px 14px;text-align:left;border-bottom:1px solid #eef0f3}
        th.num,td.num{text-align:right;font-variant-numeric:tabular-nums}
        td.num{font-family:ui-monospace,SFMono-Regular,Menlo,monospace}
        th{font-size:12px;text-transform:uppercase;letter-spacing:.04em;color:#6b7280;background:#fafbfc}
        td.bad{color:#c0392b;font-weight:700}
        tr.total td{font-weight:700;border-top:2px solid #e4e7eb;background:#fafbfc}
        tr:last-child td{border-bottom:none}
        .err{background:#fde8e8;color:#8a1f1f;padding:12px 16px;border-radius:10px;margin-bottom:16px}
        .empty{color:#6b7280}
        @media(prefers-color-scheme:dark){
          body{background:#0f1215;color:#e4e7eb}
          dl,table{background:#171b1f;border-color:#2a2f36}
          th{background:#1b2025;color:#9aa4af}
          th,td{border-color:#232830}
          tr.total td{background:#1b2025;border-top-color:#2a2f36}
          code{background:#232830}
          .badge.ok{background:#12331f;color:#4ade80}.badge.fail{background:#3a1717;color:#f87171}.badge.run{background:#332813;color:#fbbf24}
          .tag.dry{background:#1e2340;color:#a5b4fc}.tag.apply{background:#14312a;color:#6ee7b7}
          .err{background:#3a1717;color:#fca5a5}
        }
        """;
}
