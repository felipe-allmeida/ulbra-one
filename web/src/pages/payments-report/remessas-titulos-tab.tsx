import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  type RemittanceCategory,
  type RemittanceLineStatus,
  type RemittanceStatus,
  type RemittanceListItem,
  type RemittanceLineItem,
  type RemittanceLine,
} from '../../api';
import { useRemittance, useRemittances, useRemittanceLines, useUnits, useCostCenters } from '../../queries';
import { brl } from '../../lib';
import { fmtDate, fmtDateTime } from '../../datetime';
import { usePageParam, useResetPageOnChange, useOriginNav } from '../../nav';
import { Button, Modal, Pager, Panel, Select, SkeletonRows, ToolbarField } from '../../ui';
import { RemittanceCategoryBadge, RemittanceLineStatusBadge, remittanceCategoryLabel } from '../remessas/badges';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { usePhone } from '../../useMediaQuery';

const CATEGORIES: RemittanceCategory[] = ['Pix', 'Ted', 'BoletoBancario', 'Convenio'];
const REM_STATUS: RemittanceStatus[] = ['Generated', 'Confirmed', 'PartiallyReturned', 'Cancelled'];
const LINE_STATUS: RemittanceLineStatus[] = ['Pending', 'Paid', 'Returned'];

const sub = { color: 'var(--muted-foreground)', fontSize: 11, marginTop: 2 } as const;

/** Aba "Remessas / Títulos" — melhor visualização do que foi gerado nas remessas CNAB. Dois modos:
 *  Remessas (lista + drill-down nos títulos) e Títulos gerados (lista plana, filtrável por unidade). */
export function RemessasTitulosTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [view, setView] = useState<'remessas' | 'titulos'>('remessas');
  const [page, setPage] = usePageParam();

  const [category, setCategory] = useState<RemittanceCategory | ''>('');
  const [status, setStatus] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [unitId, setUnitId] = useState('');
  const [costCenterId, setCostCenterId] = useState('');
  const [detailId, setDetailId] = useState<string | null>(null);

  useResetPageOnChange(setPage, [view, category, status, from, to, unitId, costCenterId]);

  const units = useUnits(companyId).data ?? [];
  const costCenters = useCostCenters(companyId).data ?? [];

  const switchView = (v: 'remessas' | 'titulos') => {
    setView(v);
    setStatus(''); // remittance-status and line-status options differ between the views
  };

  return (
    <>
      <div style={{ display: 'flex', gap: 8, margin: '4px 0 12px' }}>
        <Button variant={view === 'remessas' ? 'brand' : 'ghost'} onClick={() => switchView('remessas')}>
          {t('paymentsReport.remessas.viewRemittances')}
        </Button>
        <Button variant={view === 'titulos' ? 'brand' : 'ghost'} onClick={() => switchView('titulos')}>
          {t('paymentsReport.remessas.viewTitulos')}
        </Button>
      </div>

      <Panel title={t('paymentsReport.remessas.filtersTitle')} padded>
        <div className='rtt-filters-row'>
          {view === 'titulos' && (
            <>
              <ToolbarField label={t('paymentsReport.filters.unit')} className='rtt-field-180'>
                <Select value={unitId} onChange={(e) => setUnitId(e.target.value)}>
                  <option value=''>{t('paymentsReport.filters.allUnits')}</option>
                  {units.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.name}
                    </option>
                  ))}
                </Select>
              </ToolbarField>
              <ToolbarField label={t('paymentsReport.filters.nature')} className='rtt-field-180'>
                <Select value={costCenterId} onChange={(e) => setCostCenterId(e.target.value)}>
                  <option value=''>{t('paymentsReport.filters.allNatures')}</option>
                  {costCenters.map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </Select>
              </ToolbarField>
            </>
          )}
          <ToolbarField label={t('paymentsReport.remessas.category')} className='rtt-field-150'>
            <Select value={category} onChange={(e) => setCategory(e.target.value as RemittanceCategory | '')}>
              <option value=''>{t('paymentsReport.remessas.allCategories')}</option>
              {CATEGORIES.map((c) => (
                <option key={c} value={c}>
                  {remittanceCategoryLabel(c, t)}
                </option>
              ))}
            </Select>
          </ToolbarField>
          <ToolbarField label={t('paymentsReport.filters.status')} className='rtt-field-180'>
            <Select value={status} onChange={(e) => setStatus(e.target.value)}>
              <option value=''>{t('paymentsReport.remessas.allStatus')}</option>
              {view === 'remessas'
                ? REM_STATUS.map((s) => (
                    <option key={s} value={s}>
                      {t(`remittances.status.${s}`)}
                    </option>
                  ))
                : LINE_STATUS.map((s) => (
                    <option key={s} value={s}>
                      {t(`remittances.lineStatus.${s}`)}
                    </option>
                  ))}
            </Select>
          </ToolbarField>
          <ToolbarField label={t('paymentsReport.filters.dueFrom')} className='rtt-field-150'>
            <input type='date' className='input' value={from} onChange={(e) => setFrom(e.target.value)} />
          </ToolbarField>
          <ToolbarField label={t('paymentsReport.filters.dueTo')} className='rtt-field-150'>
            <input type='date' className='input' value={to} onChange={(e) => setTo(e.target.value)} />
          </ToolbarField>
        </div>
      </Panel>

      {view === 'remessas' ? (
        <RemessasList
          companyId={companyId}
          category={category || undefined}
          status={(status as RemittanceStatus) || undefined}
          from={from || undefined}
          to={to || undefined}
          page={page}
          setPage={setPage}
          onOpen={setDetailId}
        />
      ) : (
        <TitulosList
          companyId={companyId}
          unitId={unitId || undefined}
          costCenterId={costCenterId || undefined}
          category={category || undefined}
          status={(status as RemittanceLineStatus) || undefined}
          from={from || undefined}
          to={to || undefined}
          page={page}
          setPage={setPage}
        />
      )}

      {detailId && <RemittanceDetailModal id={detailId} onClose={() => setDetailId(null)} />}
    </>
  );
}

function RemessasList(props: {
  companyId: string;
  category?: RemittanceCategory;
  status?: RemittanceStatus;
  from?: string;
  to?: string;
  page: number;
  setPage: (p: number) => void;
  onOpen: (id: string) => void;
}) {
  const { t } = useTranslation();
  const list = useRemittances(
    {
      companyId: props.companyId,
      category: props.category,
      status: props.status,
      from: props.from,
      to: props.to,
      page: props.page,
    },
    { enabled: !!props.companyId },
  );
  const items = list.data?.items ?? [];
  return (
    <Panel title={t('paymentsReport.remessas.remittancesTitle', { count: list.data?.total ?? 0 })}>
      {list.isLoading || items.length === 0 ? (
        <div className='doc-table-wrap'>
          <table className='doc-table'>
            <thead>
              <tr>
                <th>{t('paymentsReport.remessas.seq')}</th>
                <th>{t('paymentsReport.remessas.category')}</th>
                <th>{t('paymentsReport.remessas.generatedAt')}</th>
                <th>{t('paymentsReport.remessas.lineCount')}</th>
                <th>{t('paymentsReport.remessas.total')}</th>
                <th>{t('paymentsReport.filters.status')}</th>
                <th aria-label='' />
              </tr>
            </thead>
            <tbody>
              {list.isLoading ? (
                <SkeletonRows rows={6} cols={7} />
              ) : (
                <tr>
                  <td colSpan={7} style={{ textAlign: 'center', color: 'var(--muted-foreground)', padding: 24 }}>
                    {t('paymentsReport.remessas.empty')}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      ) : (
        <div className='doc-table-wrap'>
          <RemessasListTable items={items} onOpen={props.onOpen} />
        </div>
      )}
      <Pager page={props.page} totalPages={list.data?.totalPages ?? 1} onPage={props.setPage} />
    </Panel>
  );
}

/**
 * Ordem = seq/category/generatedAt/lineCount/total/status/actions, igual a hoje. `seq` é o `title`
 * (não há outra identidade melhor para a remessa); `generatedAt`+`lineCount` são os dois `meta`;
 * `category` e `status` (aqui texto simples, não badge — ainda assim um papel `status` genuíno)
 * empilham como `status`. Sem `onRowClick` aqui: a navegação sempre foi só pelo botão "Ver títulos".
 */
function RemessasListTable({ items, onOpen }: { items: RemittanceListItem[]; onOpen: (id: string) => void }) {
  const { t } = useTranslation();
  const columns: Column<RemittanceListItem>[] = [
    { key: 'seq', header: t('paymentsReport.remessas.seq'), role: 'title', cell: (r) => <>#{r.sequenceNumber}</> },
    {
      key: 'category',
      header: t('paymentsReport.remessas.category'),
      role: 'status',
      cell: (r) => <RemittanceCategoryBadge category={r.category} />,
    },
    {
      key: 'generatedAt',
      header: t('paymentsReport.remessas.generatedAt'),
      role: 'meta',
      cell: (r) => fmtDateTime(r.generatedAt),
    },
    {
      key: 'lineCount',
      header: t('paymentsReport.remessas.lineCount'),
      role: 'meta',
      cell: (r) => <span className='num'>{r.lineCount}</span>,
    },
    {
      key: 'total',
      header: t('paymentsReport.remessas.total'),
      role: 'amount',
      cell: (r) => <span className='num'>{brl(r.totalAmount)}</span>,
    },
    {
      key: 'status',
      header: t('paymentsReport.filters.status'),
      role: 'status',
      cell: (r) => t(`remittances.status.${r.status}`),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (r) => (
        <Button variant='ghost' onClick={() => onOpen(r.id)}>
          {t('paymentsReport.remessas.lines')}
        </Button>
      ),
    },
  ];

  return <ResponsiveTable rows={items} columns={columns} rowKey={(r) => r.id} />;
}

function TitulosList(props: {
  companyId: string;
  unitId?: string;
  costCenterId?: string;
  category?: RemittanceCategory;
  status?: RemittanceLineStatus;
  from?: string;
  to?: string;
  page: number;
  setPage: (p: number) => void;
}) {
  const { t } = useTranslation();
  const openPayable = useOriginNav();
  const list = useRemittanceLines(
    {
      companyId: props.companyId,
      unitId: props.unitId,
      costCenterId: props.costCenterId,
      category: props.category,
      status: props.status,
      from: props.from,
      to: props.to,
      page: props.page,
    },
    { enabled: !!props.companyId },
  );
  const items = list.data?.items ?? [];
  return (
    <Panel title={t('paymentsReport.remessas.titulosTitle', { count: list.data?.total ?? 0 })}>
      {list.isLoading || items.length === 0 ? (
        <div className='doc-table-wrap'>
          <table className='doc-table'>
            <thead>
              <tr>
                <th>{t('paymentsReport.remessas.remittance')}</th>
                <th>{t('paymentsReport.columns.vendorDocument')}</th>
                <th>{t('paymentsReport.filters.unit')}</th>
                <th>{t('paymentsReport.filters.nature')}</th>
                <th>{t('paymentsReport.remessas.total')}</th>
                <th>{t('paymentsReport.dueShort')}</th>
                <th>{t('paymentsReport.filters.status')}</th>
                <th aria-label='' />
              </tr>
            </thead>
            <tbody>
              {list.isLoading ? (
                <SkeletonRows rows={8} cols={8} />
              ) : (
                <tr>
                  <td colSpan={8} style={{ textAlign: 'center', color: 'var(--muted-foreground)', padding: 24 }}>
                    {t('paymentsReport.remessas.titulosEmpty')}
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      ) : (
        <div className='doc-table-wrap'>
          <TitulosListTable
            items={items}
            onOpenPayable={(payableId) => void openPayable(`/app/payables/${payableId}`)}
          />
        </div>
      )}
      <Pager page={props.page} totalPages={list.data?.totalPages ?? 1} onPage={props.setPage} />
    </Panel>
  );
}

/**
 * Ordem = remittance/vendorDocument/unit/nature/total/dueShort/status/actions, igual a hoje.
 * `remittance` (seq em negrito + badge de categoria) é o `title` — mesmo padrão de conteúdo
 * composto do restante do domínio de remessas; `vendorDocument` (favorecido + código da
 * requisição, texto simples, sem negrito no original) e `unit` são os dois `meta`; `nature` e
 * `dueShort` ficam em `detail` (orçamento de meta já usado); `status` é `status`. Sem
 * `onRowClick` aqui: a navegação sempre foi só pelo botão "Ver".
 *
 * `reqCodeLine` tinha sido emendado (telefone-só) dentro da célula `detail` de `nature` — no card
 * expandido isso rotularia o código da requisição como "Natureza", um rótulo que não descreve o
 * conteúdo (mesmo defeito já rejeitado neste projeto para o rodapé de totais e corrigido em
 * `users.tsx`/commit 4e8a4240). Ganha coluna `detail` própria, só no telefone, com `header` próprio.
 */
function TitulosListTable({
  items,
  onOpenPayable,
}: {
  items: RemittanceLineItem[];
  onOpenPayable: (payableId: string) => void;
}) {
  const { t } = useTranslation();
  // `.rt-meta` forces one-line nowrap+ellipsis (ResponsiveTable.tsx) — inherited by a nested block,
  // so the requisition-code line under a long favorecido name would clip with no "…" on phone
  // (same defect found in cash-flow.tsx). It moves into `nature`'s detail cell there; desktop
  // keeps the meta cell untouched.
  const phone = usePhone();
  const reqCodeLine = (l: RemittanceLineItem) =>
    l.requisitionCode != null ? <div style={sub}>#{l.requisitionCode}</div> : null;

  const columns: Column<RemittanceLineItem>[] = [
    {
      key: 'remittance',
      header: t('paymentsReport.remessas.remittance'),
      role: 'title',
      cell: (l) => (
        <>
          <strong>#{l.remittanceSequence}</strong>
          <div style={sub}>
            <RemittanceCategoryBadge category={l.category} />
          </div>
        </>
      ),
    },
    {
      key: 'vendorDocument',
      header: t('paymentsReport.columns.vendorDocument'),
      role: 'meta',
      cell: (l) => (
        <>
          {l.favoredName}
          {!phone && reqCodeLine(l)}
        </>
      ),
    },
    { key: 'unit', header: t('paymentsReport.filters.unit'), role: 'meta', cell: (l) => l.unitName ?? '—' },
    {
      key: 'nature',
      header: t('paymentsReport.filters.nature'),
      role: 'detail',
      cell: (l) => l.natureName ?? '—',
    },
    // Só existe no telefone — no desktop o código da requisição já mora dentro de `vendorDocument`
    // (ver acima), e esta coluna nunca é adicionada ao array, então a contagem de colunas do
    // desktop não muda. Tem `header` próprio para que o painel expandido rotule o valor como
    // "Requisição", não como "Natureza" (ver comentário acima do array).
    ...(phone
      ? [
          {
            key: 'requisition',
            header: t('paymentsReport.remessas.requisition'),
            role: 'detail' as const,
            cell: (l: RemittanceLineItem) => reqCodeLine(l) ?? '—',
          },
        ]
      : []),
    {
      key: 'total',
      header: t('paymentsReport.remessas.total'),
      role: 'amount',
      cell: (l) => <span className='num'>{brl(l.amount)}</span>,
    },
    {
      key: 'dueShort',
      header: t('paymentsReport.dueShort'),
      role: 'detail',
      cell: (l) => (l.dueDate ? fmtDate(l.dueDate) : '—'),
    },
    {
      key: 'status',
      header: t('paymentsReport.filters.status'),
      role: 'status',
      cell: (l) => (
        <>
          <RemittanceLineStatusBadge s={l.status as RemittanceLineStatus} />
          {l.returnReason && <div style={sub}>{l.returnReason}</div>}
        </>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (l) => (
        <Button variant='ghost' onClick={() => onOpenPayable(l.payableId)}>
          {t('paymentsReport.view')}
        </Button>
      ),
    },
  ];

  return <ResponsiveTable rows={items} columns={columns} rowKey={(l) => l.id} />;
}

function RemittanceDetailModal({ id, onClose }: { id: string; onClose: () => void }) {
  const { t } = useTranslation();
  const { data: r, isLoading } = useRemittance(id);
  return (
    <Modal
      title={
        r ? t('paymentsReport.remessas.remittance') + ` #${r.sequenceNumber}` : t('paymentsReport.remessas.remittance')
      }
      sub={
        r
          ? `${remittanceCategoryLabel(r.category, t)} · ${brl(r.totalAmount)} · ${fmtDateTime(r.generatedAt)}`
          : undefined
      }
      size='wide'
      onClose={onClose}
      footer={
        <Button variant='ghost' onClick={onClose}>
          {t('paymentsReport.remessas.close')}
        </Button>
      }
    >
      {isLoading || !r ? (
        <div className='doc-table-wrap'>
          <table className='doc-table'>
            <tbody>
              <SkeletonRows rows={5} cols={6} />
            </tbody>
          </table>
        </div>
      ) : (
        <div className='doc-table-wrap'>
          <RemittanceDetailLinesTable lines={r.lines} />
        </div>
      )}
    </Modal>
  );
}

/**
 * Ordem = vendorDocument/unit/nature/total/dueShort/status, igual a hoje. Sem coluna de ações aqui
 * (o modal já É o detalhe — nada mais para abrir). `vendorDocument` (favorecido) é o `title` — a
 * única identidade disponível nesta grade, já que a remessa em si é o título do modal; `unit` é o
 * único `meta` que sobra depois do title; `nature` e `dueShort` ficam em `detail`; `status` é
 * `status`. Sem `onRowClick`: nunca houve navegação de linha aqui.
 */
function RemittanceDetailLinesTable({ lines }: { lines: RemittanceLine[] }) {
  const { t } = useTranslation();
  const columns: Column<RemittanceLine>[] = [
    {
      key: 'vendorDocument',
      header: t('paymentsReport.columns.vendorDocument'),
      role: 'title',
      cell: (l) => l.favoredName,
    },
    { key: 'unit', header: t('paymentsReport.filters.unit'), role: 'meta', cell: (l) => l.unitName ?? '—' },
    { key: 'nature', header: t('paymentsReport.filters.nature'), role: 'detail', cell: (l) => l.natureName ?? '—' },
    {
      key: 'total',
      header: t('paymentsReport.remessas.total'),
      role: 'amount',
      cell: (l) => <span className='num'>{brl(l.amount)}</span>,
    },
    {
      key: 'dueShort',
      header: t('paymentsReport.dueShort'),
      role: 'detail',
      cell: (l) => (l.dueDate ? fmtDate(l.dueDate) : '—'),
    },
    {
      key: 'status',
      header: t('paymentsReport.filters.status'),
      role: 'status',
      cell: (l) => (
        <>
          <RemittanceLineStatusBadge s={l.status as RemittanceLineStatus} />
          {l.returnReason && <div style={sub}>{l.returnReason}</div>}
        </>
      ),
    },
  ];

  return <ResponsiveTable rows={lines} columns={columns} rowKey={(l) => l.id} />;
}
