import { useTranslation } from 'react-i18next';
import { type ProjectMetrics } from '../../api';
import { brl } from '../../lib';
import { Kpi, Panel } from '../../ui';
import { clampPct } from './budget-display';

/** Card de acompanhamento do project vinculado (teto acumulado). Interino: `previsto`/`realizado`
 * vêm de ProjectMetrics (proxy por requisições) até a integração Finance (AEL-793). */
export function ProjectBudgetCard({ metrics }: { metrics: ProjectMetrics }) {
  const { t } = useTranslation();
  return (
    <Panel title={t('requisitions.projectCard.title', { name: metrics.name })} padded>
      <div className='kpi-grid'>
        <Kpi label={t('requisitions.projectCard.budgeted')} value={brl(metrics.budgetLimit)} />
        <Kpi
          label={t('requisitions.projectCard.consumed')}
          value={brl(metrics.realizado)}
          foot={t('requisitions.projectCard.percentOfCeiling', { pct: metrics.percentRealizado })}
        />
        <Kpi
          label={t('requisitions.projectCard.forecast')}
          value={brl(metrics.previsto)}
          foot={t('requisitions.projectCard.percentOfCeiling', { pct: metrics.percentPrevisto })}
        />
        <Kpi
          label={t('requisitions.projectCard.balance')}
          value={brl(metrics.saldo)}
          foot={metrics.isOverCeiling ? t('requisitions.projectCard.overCeiling') : undefined}
        />
      </div>
      {/* Barra: realizado (verde) + previsto a consumir (azul) sobre o teto. */}
      <div
        style={{
          display: 'flex',
          width: '100%',
          height: 8,
          borderRadius: 999,
          overflow: 'hidden',
          background: 'var(--border)',
          marginTop: 12,
        }}
      >
        <div style={{ width: `${clampPct(metrics.percentRealizado)}%`, background: 'var(--success)' }} />
        <div
          style={{
            width: `${clampPct(Math.max(0, metrics.percentPrevisto - metrics.percentRealizado))}%`,
            background: 'var(--info)',
          }}
        />
      </div>
      {metrics.isOverCeiling && (
        <div className='proj-alert' role='alert' style={{ marginTop: 8 }}>
          <span>
            {t('requisitions.projectCard.overCeilingBy', { amount: brl(metrics.previsto - metrics.budgetLimit) })}
          </span>
        </div>
      )}
    </Panel>
  );
}
