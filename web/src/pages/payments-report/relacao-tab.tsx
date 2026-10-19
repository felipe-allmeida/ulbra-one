import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FileText, MessageSquare } from 'lucide-react';
import { api, type PayableStatus, type PaymentsReportParams } from '../../api';
import { usePaymentsReport, useUnits, useCostCenters } from '../../queries';
import { brl } from '../../lib';
import { fmtDate, fmtDateTime } from '../../datetime';
import { usePageParam, useResetPageOnChange, useOriginNav } from '../../nav';
import { useToast } from '../../toast';
import { Button, Modal, MultiSelect, Pager, Panel, SkeletonRows, ToolbarField } from '../../ui';
import { downloadWithAuth } from '../../download';
import { formatVendorDocument } from '../treasury/queue-groups';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import type { PaymentReportItem } from '../../api';
import { usePhone } from '../../useMediaQuery';

// Status options for the filter — PayableStatus → the same pt-BR labels the report rows carry.
/**
 * Rótulos do filtro, fiéis à tela legada — por isso NÃO saem de `payables.status.*`, que usa os termos
 * do redesenho ("Rejeitada", "Devolvida"). A divergência é deliberada e local a este relatório; mantenha
 * as duas listas em sincronia ao mexer nos desfechos.
 */
const STATUS_OPTIONS: { value: PayableStatus; label: string }[] = [
  { value: 'Scheduled', label: 'Fila Pagamento' },
  { value: 'PendingApproval', label: 'Pendente Aprovação' },
  { value: 'PendingClassification', label: 'Em Triagem' },
  { value: 'PendingTaxAnalysis', label: 'Avaliação Imposto' },
  { value: 'PendingAccounting', label: 'Em Contabilização' },
  { value: 'Paid', label: 'Pago' },
  { value: 'Rejected', label: 'Devolvido' },
  { value: 'Cancelled', label: 'Rejeitado' },
  { value: 'Received', label: 'Recebido' },
];

const money = (v: number) => `R$ ${v.toLocaleString('pt-BR', { minimumFractionDigits: 2 })}`;

/** Aba "Relação de Pagamentos" — the filterable per-installment list, faithful to the legacy screen,
 *  with Copiar Resumo (WhatsApp text) and Exportar PDF. */
export function RelacaoTab({ companyId, scopeReady }: { companyId: string; scopeReady: boolean }) {
  const { t } = useTranslation();
  const toast = useToast();
  const openPayable = useOriginNav();
  const [page, setPage] = usePageParam();

  const [unitIds, setUnitIds] = useState<string[]>([]);
  const [natures, setNatures] = useState<string[]>([]);
  const [statuses, setStatuses] = useState<PayableStatus[]>([]);
  const [dueFrom, setDueFrom] = useState('');
  const [dueTo, setDueTo] = useState('');
  const [payFrom, setPayFrom] = useState('');
  const [payTo, setPayTo] = useState('');
  const [showImported, setShowImported] = useState(false);
  const [search, setSearch] = useState('');
  const [exportOpen, setExportOpen] = useState(false);
  const [exportDesc, setExportDesc] = useState(false);
  const [busy, setBusy] = useState(false);

  useResetPageOnChange(setPage, [
    companyId,
    unitIds.join(','),
    natures.join(','),
    statuses.join(','),
    dueFrom,
    dueTo,
    payFrom,
    payTo,
    showImported,
    search,
  ]);

  const units = useUnits(companyId).data ?? [];
  const costCenters = useCostCenters(companyId).data ?? [];
  // Naturezas distintas por NOME: a mesma natureza existe uma vez por empresa (ex.: "Ensino"), então na
  // visão consolidada os cost centers repetem o nome — deduplica para não listar "Ensino" N vezes.
  const natureOptions = [...new Set(costCenters.map((c) => c.name))].sort((a, b) => a.localeCompare(b, 'pt-BR'));

  const filters: PaymentsReportParams = {
    companyId,
    unitIds: unitIds.length ? unitIds : undefined,
    natures: natures.length ? natures : undefined,
    statuses: statuses.length ? statuses : undefined,
    dueFrom: dueFrom || undefined,
    dueTo: dueTo || undefined,
    payFrom: payFrom || undefined,
    payTo: payTo || undefined,
    showImported: showImported || undefined,
    search: search || undefined,
  };

  const list = usePaymentsReport({ ...filters, page }, { enabled: scopeReady });
  const items = list.data?.items ?? [];
  const total = list.data?.total ?? 0;

  const copyResumo = async () => {
    try {
      setBusy(true);
      const s = await api.paymentsReportSummary(filters);
      let text = '*RESUMO DE PAGAMENTOS* 📊\n\n';
      text += `*Quantidade de pagamentos:* ${s.count}\n`;
      text += `*Valor Total:* ${money(s.total)}\n\n`;
      text += '*VALOR POR UNIDADE:* 🏢\n';
      for (const u of s.byUnit) text += `• *${u.name}:* ${money(u.total)}\n`;
      text += '\n*VALOR POR FORNECEDOR:* 👤\n';
      for (const p of s.byProvider) text += `• *${p.name}:* ${money(p.total)}\n`;
      await navigator.clipboard.writeText(text);
      toast.success(t('paymentsReport.copied'));
    } catch {
      toast.error(t('paymentsReport.copyError'));
    } finally {
      setBusy(false);
    }
  };

  const doExport = async () => {
    try {
      setBusy(true);
      await downloadWithAuth(api.paymentsReportExportUrl({ ...filters, showDescription: exportDesc }));
      setExportOpen(false);
    } catch {
      toast.error(t('paymentsReport.exportError'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <>
      <Panel title={t('paymentsReport.filtersTitle')} padded>
        <div className='pr-filters-row'>
          {/* Convenção de UI: Pesquisar é sempre o primeiro campo. */}
          <ToolbarField label={t('paymentsReport.filters.search')} className='pr-search-field'>
            <input
              className='input'
              placeholder={t('paymentsReport.filters.searchPlaceholder')}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
          </ToolbarField>
          {/* Unidade / Natureza / Status são multi-seleção (selectButtons): refletem na tabela e no PDF. */}
          <ToolbarField label={t('paymentsReport.filters.unit')}>
            <MultiSelect
              width={190}
              options={units.map((u) => ({ value: u.id, label: u.name }))}
              selected={unitIds}
              onChange={setUnitIds}
              allLabel={t('paymentsReport.filters.allUnits')}
              countLabel={(n) => `${n} unidades`}
            />
          </ToolbarField>
          <ToolbarField label={t('paymentsReport.filters.nature')}>
            <MultiSelect
              width={190}
              options={natureOptions.map((n) => ({ value: n, label: n }))}
              selected={natures}
              onChange={setNatures}
              allLabel={t('paymentsReport.filters.allNatures')}
              countLabel={(n) => `${n} naturezas`}
            />
          </ToolbarField>
          <ToolbarField label={t('paymentsReport.filters.status')}>
            <MultiSelect
              width={190}
              options={STATUS_OPTIONS.map((s) => ({ value: s.value, label: s.label }))}
              selected={statuses}
              onChange={(v) => setStatuses(v as PayableStatus[])}
              allLabel={t('paymentsReport.filters.allStatus')}
              countLabel={(n) => `${n} status`}
            />
          </ToolbarField>
          {/* Datas/períodos (Vencimento + Pagamento) sempre juntos: agrupados num contêiner próprio para
              não se separarem quando a barra quebra linha. */}
          <div className='pr-dates-row'>
            <ToolbarField label={t('paymentsReport.filters.dueFrom')} className='pr-date-field'>
              <input type='date' className='input' value={dueFrom} onChange={(e) => setDueFrom(e.target.value)} />
            </ToolbarField>
            <ToolbarField label={t('paymentsReport.filters.dueTo')} className='pr-date-field'>
              <input type='date' className='input' value={dueTo} onChange={(e) => setDueTo(e.target.value)} />
            </ToolbarField>
            <ToolbarField label={t('paymentsReport.filters.payFrom')} className='pr-date-field'>
              <input type='date' className='input' value={payFrom} onChange={(e) => setPayFrom(e.target.value)} />
            </ToolbarField>
            <ToolbarField label={t('paymentsReport.filters.payTo')} className='pr-date-field'>
              <input type='date' className='input' value={payTo} onChange={(e) => setPayTo(e.target.value)} />
            </ToolbarField>
          </div>
          <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 13, paddingBottom: 6 }}>
            <input type='checkbox' checked={showImported} onChange={(e) => setShowImported(e.target.checked)} />
            {t('paymentsReport.filters.showImported')}
          </label>
        </div>
      </Panel>

      <Panel
        title={t('paymentsReport.listTitle', { count: total })}
        action={
          <div style={{ display: 'flex', gap: 8 }}>
            <Button
              variant='ghost'
              icon={<MessageSquare size={14} />}
              onClick={() => void copyResumo()}
              disabled={busy || items.length === 0}
            >
              {t('paymentsReport.copySummary')}
            </Button>
            <Button
              variant='brand'
              icon={<FileText size={14} />}
              onClick={() => setExportOpen(true)}
              disabled={busy || items.length === 0}
            >
              {t('paymentsReport.exportPdf')}
            </Button>
          </div>
        }
      >
        {list.isLoading || items.length === 0 ? (
          <div className='doc-table-wrap'>
            <table className='doc-table'>
              <thead>
                <tr>
                  <th>{t('paymentsReport.columns.codeRequester')}</th>
                  <th>{t('paymentsReport.columns.unitNature')}</th>
                  <th>{t('paymentsReport.columns.approverDate')}</th>
                  <th>{t('paymentsReport.columns.vendorDocument')}</th>
                  <th>{t('paymentsReport.columns.amountDue')}</th>
                  <th>{t('paymentsReport.columns.status')}</th>
                  <th aria-label={t('paymentsReport.columns.actions')} />
                </tr>
              </thead>
              <tbody>
                {list.isLoading ? (
                  <SkeletonRows rows={8} cols={7} />
                ) : (
                  <tr>
                    <td colSpan={7} style={{ textAlign: 'center', color: 'var(--muted-foreground)', padding: 24 }}>
                      {t('paymentsReport.empty')}
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        ) : (
          <div className='doc-table-wrap'>
            <RelacaoTable items={items} onOpenPayable={openPayable} />
          </div>
        )}
        <Pager page={page} totalPages={list.data?.totalPages ?? 1} onPage={setPage} />
      </Panel>

      {exportOpen && (
        <Modal
          title={t('paymentsReport.exportTitle')}
          sub={t('paymentsReport.exportSub')}
          onClose={() => setExportOpen(false)}
          footer={
            <>
              <Button variant='ghost' onClick={() => setExportOpen(false)} disabled={busy}>
                {t('common.cancel')}
              </Button>
              <Button variant='brand' icon={<FileText size={14} />} onClick={() => void doExport()} disabled={busy}>
                {t('paymentsReport.exportPdf')}
              </Button>
            </>
          }
        >
          <label style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 14 }}>
            <input type='checkbox' checked={exportDesc} onChange={(e) => setExportDesc(e.target.checked)} />
            {t('paymentsReport.includeDescriptions')}
          </label>
        </Modal>
      )}
    </>
  );
}

/**
 * Ordem = a ordem já renderizada hoje: codeRequester/unitNature/approverDate/vendorDocument/
 * amountDue/status/actions. Todas as colunas substantivas já bundlam um valor principal + uma
 * sub-linha de contexto — nenhuma é um valor simples de uma linha só (o formato que `meta` espera,
 * junto num único `·`); por isso o orçamento de `meta` fica em 0 aqui e o resto vai para `detail`,
 * exceto: `vendorDocument` (quem foi pago — a identidade da linha) vira `title`; `amountDue` (o
 * único valor monetário) vira `amount`; `status` fica `status`. Não há `onRowClick` aqui — a
 * navegação sempre foi só pelo botão "Ver".
 *
 * `vendorDoc`/`dueLine` tinham sido emendados (telefone-só) dentro da célula `detail` de
 * `codeRequester` — no card expandido isso rotularia CNPJ do fornecedor e data de vencimento como
 * "Cód. / Solicitante", um rótulo que cobre código+solicitante mas não documento nem vencimento
 * (mesmo defeito já rejeitado neste projeto para o rodapé de totais e corrigido em `users.tsx`/
 * commit 4e8a4240). Cada um ganha sua própria coluna `detail`, só no telefone, com `header` próprio.
 */
function RelacaoTable({ items, onOpenPayable }: { items: PaymentReportItem[]; onOpenPayable: (path: string) => void }) {
  const { t } = useTranslation();
  const sub = { color: 'var(--muted-foreground)', fontSize: 11, marginTop: 2 } as const;
  // `.rt-title`/`.rt-amount` force one-line nowrap+ellipsis (ResponsiveTable.tsx) — inherited by
  // nested blocks, so a document/due-date sub-line under a long vendor name or amount would clip
  // with no "…" on phone (checked at 390px, same defect found in cash-flow.tsx). Both move into
  // `codeRequester`'s detail cell there; desktop keeps title/amount untouched.
  const phone = usePhone();
  const vendorDoc = (r: PaymentReportItem) => <div style={sub}>{formatVendorDocument(r.vendorDocument)}</div>;
  const dueLine = (r: PaymentReportItem) => (
    <div style={sub}>
      {t('paymentsReport.dueShort')}: {fmtDate(r.dueDate)}
    </div>
  );

  const columns: Column<PaymentReportItem>[] = [
    {
      key: 'codeRequester',
      header: t('paymentsReport.columns.codeRequester'),
      role: 'detail',
      cell: (r) => (
        <>
          <strong>#{r.requisitionCode ?? '—'}</strong>
          <div style={sub}>{r.requesterName ?? '—'}</div>
        </>
      ),
    },
    // Só existem no telefone — no desktop o documento do fornecedor já mora dentro de
    // `vendorDocument` e o vencimento dentro de `amountDue` (ver ambos abaixo), e estas colunas
    // nunca são adicionadas ao array, então a contagem de colunas do desktop não muda. Cada uma tem
    // `header` próprio para que o painel expandido rotule o valor corretamente, não como
    // "Cód. / Solicitante" (ver comentário acima do array).
    ...(phone
      ? [
          {
            key: 'vendorDocumentDetail',
            header: t('paymentsReport.columns.document'),
            role: 'detail' as const,
            cell: (r: PaymentReportItem) => vendorDoc(r),
          },
          {
            key: 'dueDateDetail',
            header: t('paymentsReport.columns.dueDate'),
            role: 'detail' as const,
            cell: (r: PaymentReportItem) => (r.dueDate ? fmtDate(r.dueDate) : '—'),
          },
        ]
      : []),
    {
      key: 'unitNature',
      header: t('paymentsReport.columns.unitNature'),
      role: 'detail',
      cell: (r) => (
        <>
          {r.unitName ?? '—'}
          <div style={sub}>{r.natureName ?? '—'}</div>
        </>
      ),
    },
    {
      key: 'approverDate',
      header: t('paymentsReport.columns.approverDate'),
      role: 'detail',
      cell: (r) => (
        <>
          {r.approverName ?? '-'}
          <div style={sub}>{r.approvedAt ? fmtDateTime(r.approvedAt) : '-'}</div>
        </>
      ),
    },
    {
      key: 'vendorDocument',
      header: t('paymentsReport.columns.vendorDocument'),
      role: 'title',
      cell: (r) => (
        <>
          {r.counterpartyName}
          {!phone && vendorDoc(r)}
        </>
      ),
    },
    {
      key: 'amountDue',
      header: t('paymentsReport.columns.amountDue'),
      role: 'amount',
      cell: (r) => (
        <>
          <strong className='num'>{brl(r.amount)}</strong>
          {!phone && dueLine(r)}
        </>
      ),
    },
    {
      key: 'status',
      header: t('paymentsReport.columns.status'),
      role: 'status',
      cell: (r) => (
        <>
          <strong>{r.statusLabel}</strong>
          <div style={sub}>{r.paymentTypeLabel}</div>
        </>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (r) => (
        <Button variant='ghost' onClick={() => onOpenPayable(`/app/payables/${r.payableId}`)}>
          {t('paymentsReport.view')}
        </Button>
      ),
    },
  ];

  return <ResponsiveTable rows={items} columns={columns} rowKey={(r) => r.installmentId} />;
}
