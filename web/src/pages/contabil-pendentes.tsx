import { useTranslation } from 'react-i18next';
import { AlertTriangle } from 'lucide-react';
import type { PostingKind } from '../api';
import { usePendingPostings } from '../queries';
import { useApp } from '../store';
import { brlCents } from '../lib';
import { EmptyState, PageHeader, Panel } from '../ui';
import { EmpresaPicker } from './shared';
import { fmtDateTime } from '../datetime';

function kindBadge(kind: PostingKind) {
  return kind === 'Incurral' ? 'badge-pendente' : kind === 'Settlement' ? 'badge-aprovado' : 'badge-rejeitado';
}

export function ContabilPendentesPage() {
  const { t } = useTranslation();
  const { empresa, empresaId } = useApp();

  const list = usePendingPostings({ companyId: empresaId || undefined }, { enabled: !!empresaId });
  const items = list.data?.items ?? [];
  const total = list.data?.total ?? 0;

  return (
    <>
      <PageHeader title={t('accounting.pendingPostings.title')} sub={t('accounting.pendingPostings.subtitle')}>
        <EmpresaPicker />
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<AlertTriangle />}
          title={t('accounting.pendingPostings.empty.noCompanyTitle')}
          sub={t('accounting.pendingPostings.empty.noCompanySub')}
        />
      ) : (
        <Panel title={t('accounting.pendingPostings.count', { count: total })}>
          {list.isLoading ? (
            <div className='help'>{t('accounting.pendingPostings.loading')}</div>
          ) : items.length === 0 ? (
            <div className='empty-state' style={{ padding: 24 }}>
              <div className='t'>{t('accounting.pendingPostings.empty.title')}</div>
              <div className='s'>{t('accounting.pendingPostings.empty.sub')}</div>
            </div>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>{t('accounting.pendingPostings.columns.kind')}</th>
                  <th style={{ textAlign: 'right' }}>{t('accounting.pendingPostings.columns.amount')}</th>
                  <th>{t('accounting.pendingPostings.columns.reason')}</th>
                  <th>{t('accounting.pendingPostings.columns.intendedCodes')}</th>
                  <th>{t('accounting.pendingPostings.columns.sourceKey')}</th>
                  <th>{t('accounting.pendingPostings.columns.createdAt')}</th>
                </tr>
              </thead>
              <tbody>
                {items.map((p) => (
                  <tr key={p.id}>
                    <td>
                      <span className={`badge-status ${kindBadge(p.kind)}`}>
                        {t(`accounting.pendingPostings.postingKind.${p.kind}`)}
                      </span>
                    </td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {brlCents(p.amount)}
                    </td>
                    <td>{p.reason}</td>
                    <td className='t-mono'>{p.intendedCodes}</td>
                    <td className='t-mono'>{p.sourceKey}</td>
                    <td className='t-mono'>{fmtDateTime(p.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </Panel>
      )}
    </>
  );
}
