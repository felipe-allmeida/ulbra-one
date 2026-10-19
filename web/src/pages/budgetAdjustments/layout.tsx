import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { Plus, Scale } from 'lucide-react';
import { type BudgetAdjustmentStatus, type BudgetAdjustmentType } from '../../api';
import { useApp } from '../../store';
import { useBudgetAdjustments, useCostCenters, useUnits } from '../../queries';
import { usePageParam, useResetPageOnChange } from '../../nav';
import { P, usePermissions } from '../../permissions';
import { Button, EmptyState, Kpi, PageHeader, Select, ToolbarField } from '../../ui';
import { brl } from '../../lib';
import { ADEQUACAO_TYPES } from '../schemas';
import { BudgetAdjustmentTable } from './table';

const STATUSES: BudgetAdjustmentStatus[] = ['AwaitingTriage', 'AwaitingApproval', 'Approved', 'Reversed', 'Cancelled'];

export function BudgetAdjustmentsLayout() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { empresa, empresaId } = useApp();
  const { can } = usePermissions();
  const [unitId, setUnitId] = useState('');
  const [costCenterId, setCostCenterId] = useState('');
  const [status, setStatus] = useState<BudgetAdjustmentStatus | ''>('');
  const [type, setType] = useState<BudgetAdjustmentType | ''>('');
  const [page, setPage] = usePageParam();
  useResetPageOnChange(setPage, [empresaId, unitId, costCenterId, status, type]);

  const unidades = useUnits(empresaId).data ?? [];
  const centros = useCostCenters(empresaId).data ?? [];
  const list = useBudgetAdjustments(
    {
      companyId: empresaId,
      unitId: unitId || undefined,
      costCenterId: costCenterId || undefined,
      status: status || undefined,
      type: type || undefined,
      page,
    },
    { enabled: !!empresaId },
  );
  const data = list.data;
  const items = data?.items ?? [];
  const totalValue = items.reduce((s, a) => s + a.value, 0);

  return (
    <>
      <PageHeader title={t('budgetAdjustments.title')} sub={t('budgetAdjustments.subtitle')}>
        {can(P.budgetAdjustmentsWrite) && (
          <Button
            variant='brand'
            icon={<Plus />}
            disabled={!empresa}
            onClick={() => nav('/app/budget-adjustments/new')}
          >
            {t('budgetAdjustments.new')}
          </Button>
        )}
      </PageHeader>

      {!empresa ? (
        <EmptyState
          icon={<Scale />}
          title={t('budgetAdjustments.empty.noCompany')}
          sub={t('budgetAdjustments.empty.noCompanySub')}
        />
      ) : (
        <>
          <div className='toolbar' style={{ marginBottom: 12 }}>
            <ToolbarField label={t('budgetAdjustments.filters.unit')}>
              <Select value={unitId} onChange={(e) => setUnitId(e.target.value)}>
                <option value=''>{t('budgetAdjustments.filters.allUnits')}</option>
                {unidades.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.acronym}
                  </option>
                ))}
              </Select>
            </ToolbarField>
            <ToolbarField label={t('budgetAdjustments.filters.costCenter')}>
              <Select value={costCenterId} onChange={(e) => setCostCenterId(e.target.value)}>
                <option value=''>{t('budgetAdjustments.filters.allCostCenters')}</option>
                {centros.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </Select>
            </ToolbarField>
            <ToolbarField label={t('budgetAdjustments.filters.type')}>
              <Select value={type} onChange={(e) => setType(e.target.value as BudgetAdjustmentType | '')}>
                <option value=''>{t('budgetAdjustments.filters.allTypes')}</option>
                {ADEQUACAO_TYPES.map((ty) => (
                  <option key={ty} value={ty}>
                    {t(`budgetAdjustments.type.${ty}`)}
                  </option>
                ))}
              </Select>
            </ToolbarField>
            <ToolbarField label={t('budgetAdjustments.filters.status')}>
              <Select value={status} onChange={(e) => setStatus(e.target.value as BudgetAdjustmentStatus | '')}>
                <option value=''>{t('budgetAdjustments.filters.allStatuses')}</option>
                {STATUSES.map((s) => (
                  <option key={s} value={s}>
                    {t(`budgetAdjustments.status.${s}`)}
                  </option>
                ))}
              </Select>
            </ToolbarField>
          </div>

          <div className='kpi-grid'>
            <Kpi label={t('budgetAdjustments.kpis.count')} value={String(data?.total ?? 0)} />
            <Kpi
              label={t('budgetAdjustments.kpis.pageValue')}
              value={brl(totalValue)}
              foot={t('budgetAdjustments.kpis.onThisPage')}
            />
          </div>

          <BudgetAdjustmentTable
            companyId={empresaId}
            items={items}
            total={data?.total ?? 0}
            page={page}
            totalPages={data?.totalPages ?? 1}
            onPage={setPage}
            loading={list.isLoading}
          />
        </>
      )}
    </>
  );
}
