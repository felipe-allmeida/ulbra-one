import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollText } from 'lucide-react';
import { useFiscalAssessment } from '../../../queries';
import type { WithholdingFactRow } from '../../../api';
import { brlCents } from '../../../lib';
import { EmptyState, PageHeader } from '../../../ui';
import { ConsolidatedPicker } from '../../contabil-shared';
import { GuiaTable } from './by-guia-table';
import { ByCompanyTable } from './by-company-table';
import { ByCostCenterTable } from './by-cost-center-table';
import { AssessmentDrillDrawer } from './drill-drawer';

const thisMonth = () => new Date().toISOString().slice(0, 7); // YYYY-MM

/** Painel de Obrigações Acessórias: apuração de retenções por competência (agnóstica de formato).
 * Eixos: por guia (RetentionGroup), por empresa/estabelecimento (fiscal) e por centro de custo (gerencial). */
export function ObrigacoesPage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  const [perApur, setPerApur] = useState(thisMonth);
  // The dimension row the user drilled into — its underlying withholding facts feed the drawer.
  const [drill, setDrill] = useState<{ title: string; facts: WithholdingFactRow[] } | null>(null);

  const query = useFiscalAssessment({ perApur, companyId: companyId || undefined });
  const data = query.data;
  const hasData = (data?.facts.length ?? 0) > 0;

  return (
    <>
      <PageHeader title={t('fiscal.obrigacoes.title')} sub={t('fiscal.obrigacoes.subtitle')}>
        <input
          type='month'
          value={perApur}
          onChange={(e) => setPerApur(e.target.value || thisMonth())}
          aria-label={t('fiscal.obrigacoes.competencia')}
        />
        <ConsolidatedPicker value={companyId} onChange={setCompanyId} />
      </PageHeader>

      {query.isLoading ? (
        <div className='help'>{t('accounting.journal.loading')}</div>
      ) : !hasData ? (
        <EmptyState
          icon={<ScrollText />}
          title={t('fiscal.obrigacoes.empty.title')}
          sub={t('fiscal.obrigacoes.empty.sub')}
        />
      ) : (
        <>
          <div className='info-grid' style={{ marginBottom: 16 }}>
            <div className='info-card'>
              <div className='info-label'>{t('fiscal.obrigacoes.base')}</div>
              <div className='info-value'>{brlCents(data?.totals.baseTotal ?? 0)}</div>
            </div>
            <div className='info-card'>
              <div className='info-label'>{t('fiscal.obrigacoes.withheld')}</div>
              <div className='info-value'>{brlCents(data?.totals.withheldTotal ?? 0)}</div>
            </div>
            <div className='info-card'>
              <div className='info-label'>{t('fiscal.obrigacoes.payables')}</div>
              <div className='info-value'>{data?.totals.payableCount ?? 0}</div>
            </div>
          </div>

          <GuiaTable rows={data?.byRetentionGroup ?? []} withheldTotal={data?.totals.withheldTotal ?? 0} />

          <ByCompanyTable
            rows={data?.byCompany ?? []}
            onDrill={(row) =>
              setDrill({
                title: row.companyName,
                facts: (data?.facts ?? []).filter((f) => f.companyId === row.companyId),
              })
            }
          />

          <ByCostCenterTable
            rows={data?.byCostCenter ?? []}
            onDrill={(row) =>
              setDrill({
                title: row.name || row.code,
                facts: (data?.facts ?? []).filter((f) => f.costCenterId === row.costCenterId),
              })
            }
          />
        </>
      )}

      <AssessmentDrillDrawer drill={drill} onClose={() => setDrill(null)} />
    </>
  );
}
