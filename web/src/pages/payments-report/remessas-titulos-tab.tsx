import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { type RemittanceCategory, type RemittanceLineStatus, type RemittanceStatus } from '../../api';
import { useRemittance, useRemittances, useRemittanceLines, useUnits, useCostCenters } from '../../queries';
import { brl } from '../../lib';
import { fmtDate, fmtDateTime } from '../../datetime';
import { usePageParam, useResetPageOnChange, useOriginNav } from '../../nav';
import { Button, Modal, Pager, Panel, Select, SkeletonRows, ToolbarField } from '../../ui';
import { RemittanceCategoryBadge, RemittanceLineStatusBadge, remittanceCategoryLabel } from '../remessas/badges';

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
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 12, alignItems: 'flex-end' }}>
          {view === 'titulos' && (
            <>
              <ToolbarField label={t('paymentsReport.filters.unit')}>
                <Select value={unitId} style={{ width: 180 }} onChange={(e) => setUnitId(e.target.value)}>
                  <option value=''>{t('paymentsReport.filters.allUnits')}</option>
                  {units.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.name}
                    </option>
                  ))}
                </Select>
              </ToolbarField>
              <ToolbarField label={t('paymentsReport.filters.nature')}>
                <Select value={costCenterId} style={{ width: 180 }} onChange={(e) => setCostCenterId(e.target.value)}>
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
          <ToolbarField label={t('paymentsReport.remessas.category')}>
            <Select
              value={category}
              style={{ width: 150 }}
              onChange={(e) => setCategory(e.target.value as RemittanceCategory | '')}
            >
              <option value=''>{t('paymentsReport.remessas.allCategories')}</option>
              {CATEGORIES.map((c) => (
                <option key={c} value={c}>
                  {remittanceCategoryLabel(c, t)}
                </option>
              ))}
            </Select>
          </ToolbarField>
          <ToolbarField label={t('paymentsReport.filters.status')}>
            <Select value={status} style={{ width: 180 }} onChange={(e) => setStatus(e.target.value)}>
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
          <ToolbarField label={t('paymentsReport.filters.dueFrom')}>
            <input
              type='date'
              className='input'
              style={{ width: 150 }}
              value={from}
              onChange={(e) => setFrom(e.target.value)}
            />
          </ToolbarField>
          <ToolbarField label={t('paymentsReport.filters.dueTo')}>
            <input
              type='date'
              className='input'
              style={{ width: 150 }}
              value={to}
              onChange={(e) => setTo(e.target.value)}
            />
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
            ) : items.length === 0 ? (
              <tr>
                <td colSpan={7} style={{ textAlign: 'center', color: 'var(--muted-foreground)', padding: 24 }}>
                  {t('paymentsReport.remessas.empty')}
                </td>
              </tr>
            ) : (
              items.map((r) => (
                <tr key={r.id}>
                  <td>
                    <strong>#{r.sequenceNumber}</strong>
                  </td>
                  <td>
                    <RemittanceCategoryBadge category={r.category} />
                  </td>
                  <td>{fmtDateTime(r.generatedAt)}</td>
                  <td className='num'>{r.lineCount}</td>
                  <td className='num'>{brl(r.totalAmount)}</td>
                  <td>{t(`remittances.status.${r.status}`)}</td>
                  <td style={{ textAlign: 'right' }}>
                    <Button variant='ghost' onClick={() => props.onOpen(r.id)}>
                      {t('paymentsReport.remessas.lines')}
                    </Button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      <Pager page={props.page} totalPages={list.data?.totalPages ?? 1} onPage={props.setPage} />
    </Panel>
  );
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
            ) : items.length === 0 ? (
              <tr>
                <td colSpan={8} style={{ textAlign: 'center', color: 'var(--muted-foreground)', padding: 24 }}>
                  {t('paymentsReport.remessas.titulosEmpty')}
                </td>
              </tr>
            ) : (
              items.map((l) => (
                <tr key={l.id}>
                  <td>
                    <strong>#{l.remittanceSequence}</strong>
                    <div style={sub}>
                      <RemittanceCategoryBadge category={l.category} />
                    </div>
                  </td>
                  <td>
                    {l.favoredName}
                    {l.requisitionCode != null && <div style={sub}>#{l.requisitionCode}</div>}
                  </td>
                  <td>{l.unitName ?? '—'}</td>
                  <td>{l.natureName ?? '—'}</td>
                  <td className='num'>{brl(l.amount)}</td>
                  <td>{l.dueDate ? fmtDate(l.dueDate) : '—'}</td>
                  <td>
                    <RemittanceLineStatusBadge s={l.status as RemittanceLineStatus} />
                    {l.returnReason && <div style={sub}>{l.returnReason}</div>}
                  </td>
                  <td style={{ textAlign: 'right' }}>
                    <Button variant='ghost' onClick={() => void openPayable(`/app/payables/${l.payableId}`)}>
                      {t('paymentsReport.view')}
                    </Button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      <Pager page={props.page} totalPages={list.data?.totalPages ?? 1} onPage={props.setPage} />
    </Panel>
  );
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
          <table className='doc-table'>
            <thead>
              <tr>
                <th>{t('paymentsReport.columns.vendorDocument')}</th>
                <th>{t('paymentsReport.filters.unit')}</th>
                <th>{t('paymentsReport.filters.nature')}</th>
                <th>{t('paymentsReport.remessas.total')}</th>
                <th>{t('paymentsReport.dueShort')}</th>
                <th>{t('paymentsReport.filters.status')}</th>
              </tr>
            </thead>
            <tbody>
              {r.lines.map((l) => (
                <tr key={l.id}>
                  <td>{l.favoredName}</td>
                  <td>{l.unitName ?? '—'}</td>
                  <td>{l.natureName ?? '—'}</td>
                  <td className='num'>{brl(l.amount)}</td>
                  <td>{l.dueDate ? fmtDate(l.dueDate) : '—'}</td>
                  <td>
                    <RemittanceLineStatusBadge s={l.status as RemittanceLineStatus} />
                    {l.returnReason && <div style={sub}>{l.returnReason}</div>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Modal>
  );
}
