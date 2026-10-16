import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { WithholdingPayableRow } from '../../api';
import { brlCents } from '../../lib';
import { useOriginState } from '../../nav';
import { Drawer } from '../../ui';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';

/** Side drawer with a payable's per-tax withholding breakdown (data already comes in the report GET). */
export function WithholdingDetailDrawer({ row, onClose }: { row: WithholdingPayableRow | null; onClose: () => void }) {
  const { t } = useTranslation();
  const originState = useOriginState();
  if (!row) return null;

  // Ordem = a ordem já renderizada hoje. tax é a única `title`. rate e base cabem juntas como `meta` (o par
  // usado no resto do módulo). realized é o único `amount`; foreseen fica em `detail`, valor monetário
  // secundário. footCell lê row.realizedAmount/row.foreseenAmount (já agregados no próprio row, não somas
  // de `rows` aqui) — mesmo dado que o <tfoot> original mostrava.
  const columns: Column<WithholdingPayableRow['lines'][number]>[] = [
    {
      key: 'tax',
      header: t('accounting.withholdings.detail.columns.tax'),
      role: 'title',
      cell: (l) => (
        <>
          {t(`payables.withholding.taxType.${l.taxType}`)}
          {l.timing === 'OnPayment' && (
            <span className='ret-row-sub'> · {t('payables.withholding.moment.onPayment')}</span>
          )}
        </>
      ),
    },
    {
      key: 'base',
      header: t('accounting.withholdings.detail.columns.base'),
      role: 'meta',
      align: 'right',
      cell: (l) => <span className='amount-cell'>{brlCents(l.baseAmount)}</span>,
    },
    {
      key: 'rate',
      header: t('accounting.withholdings.detail.columns.rate'),
      role: 'meta',
      align: 'right',
      cell: (l) => (l.rate != null ? `${(l.rate * 100).toFixed(2)}%` : '—'),
    },
    {
      key: 'realized',
      header: t('accounting.withholdings.detail.columns.realized'),
      role: 'amount',
      align: 'right',
      cell: (l) => <span className='amount-cell'>{brlCents(l.realizedAmount)}</span>,
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {brlCents(row.realizedAmount)}
        </span>
      ),
    },
    {
      key: 'foreseen',
      header: t('accounting.withholdings.detail.columns.foreseen'),
      role: 'detail',
      align: 'right',
      cell: (l) => (
        <span
          className='amount-cell'
          style={{ color: l.foreseenAmount > 0 ? 'var(--warning-foreground, inherit)' : 'var(--muted-foreground)' }}
        >
          {l.foreseenAmount > 0 ? brlCents(l.foreseenAmount) : '—'}
        </span>
      ),
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {row.foreseenAmount > 0 ? brlCents(row.foreseenAmount) : '—'}
        </span>
      ),
    },
  ];

  return (
    <Drawer open onClose={onClose} title={t('accounting.withholdings.detail.title')} sub={row.counterpartyName}>
      {/* Gross and Net side by side; Retido and A reter split the withheld total. */}
      <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)', gap: 12, marginBottom: 16 }}>
        <div className='info-card'>
          <div className='info-label'>{t('accounting.withholdings.gross')}</div>
          <div className='info-value'>{brlCents(row.totalAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('accounting.withholdings.net')}</div>
          <div className='info-value'>{brlCents(row.netTotal)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('accounting.withholdings.realized')}</div>
          <div className='info-value'>{brlCents(row.realizedAmount)}</div>
        </div>
        <div className='info-card'>
          <div className='info-label'>{t('accounting.withholdings.foreseen')}</div>
          <div className='info-value'>{row.foreseenAmount > 0 ? brlCents(row.foreseenAmount) : '—'}</div>
        </div>
      </div>

      {row.lines.length > 0 ? (
        <div className='drawer-table-scroll'>
          <ResponsiveTable
            rows={row.lines}
            columns={columns}
            rowKey={(l) => l.taxType}
            footLabel={<span style={{ fontWeight: 600 }}>{t('accounting.withholdings.total')}</span>}
          />
        </div>
      ) : (
        <div className='help'>{t('accounting.withholdings.detail.empty')}</div>
      )}

      <div style={{ marginTop: 16 }}>
        <Link className='linklike' to={`/app/payables/${row.id}`} state={originState}>
          {t('accounting.withholdings.detail.openPayable')}
        </Link>
      </div>
    </Drawer>
  );
}
