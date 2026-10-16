import { useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import { Check } from 'lucide-react';
import { type Requisition, type RequisitionStatus, type RequisitionType } from '../../api';
import { useApproveRequisition, useApproveTriage, useRequisitions, type RequisitionsParams } from '../../queries';
import { usePageParam, useResetPageOnChange } from '../../nav';
import { P, usePermissions } from '../../permissions';
import { Button } from '../../ui';
import { parseMoneyInput } from '../../lib';
import { useToast } from '../../toast';
import { getErrorMessage } from '../../apiError';
import { CLOSED_STATUSES, STAGE_STATUS, type StageKey } from './stages';
import { RequisitionTable } from './requisition-table';
import { REQUISITION_TYPES, RequisitionFilterBar, type RequisitionFilters } from './filter-bar';
import { triageApprovedToastKey } from './timeline';

function numOrUndef(s: string): number | undefined {
  // Filter values now arrive in pt-BR convention ("1.234,56") from the CurrencyInput — parse accordingly.
  return parseMoneyInput(s) ?? undefined;
}

/** Uma aba de estágio: lista as requisições do status correspondente, com barra de filtros.
 * Triagem/Aprovação ganham um botão de ação inline (encaminhar / aprovar). */
export function RequisitionStageTab({
  companyId,
  stage,
  mine,
}: {
  companyId: string;
  stage: StageKey;
  mine?: boolean;
}) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const toast = useToast();
  const [page, setPage] = usePageParam();
  const [params, setParams] = useSearchParams();

  // Filtros vivem na URL (fonte única → deep-link + voltar/avançar). Lidos a cada render.
  // Enums vindos da URL são validados — um deep-link corrompido é ignorado, nunca vira 400 na API.
  const filters: RequisitionFilters = useMemo(() => {
    const closedParam = params.get('closed') as RequisitionStatus | null;
    let statuses = (params.getAll('st') as RequisitionStatus[]).filter((s) => CLOSED_STATUSES.includes(s));
    // Deep-link legado ?closed=<status> (cards "Pagas" etc.) pré-seleciona o chip correspondente.
    if (statuses.length === 0 && closedParam && CLOSED_STATUSES.includes(closedParam)) statuses = [closedParam];
    const typeParam = params.get('type');
    return {
      search: params.get('q') ?? '',
      unitId: params.get('unit'),
      costCenterId: params.get('cc'),
      type: REQUISITION_TYPES.includes(typeParam as RequisitionType) ? (typeParam as RequisitionType) : '',
      competencyFrom: params.get('cfrom') ?? '',
      competencyTo: params.get('cto') ?? '',
      minValue: params.get('vmin') ?? '',
      maxValue: params.get('vmax') ?? '',
      statuses,
      sort: params.get('sort') ?? '',
    };
  }, [params]);

  const writeFilters = (next: RequisitionFilters) => {
    setParams(
      (prev) => {
        const set = (k: string, v: string) => {
          if (v) prev.set(k, v);
          else prev.delete(k);
        };
        set('q', next.search);
        set('unit', next.unitId ?? '');
        set('cc', next.costCenterId ?? '');
        set('type', next.type);
        set('cfrom', next.competencyFrom);
        set('cto', next.competencyTo);
        set('vmin', next.minValue);
        set('vmax', next.maxValue);
        set('sort', next.sort);
        prev.delete('closed'); // superseded by the multi-status chips
        prev.delete('st');
        next.statuses.forEach((s) => prev.append('st', s));
        prev.delete('page'); // a new filter set starts on page 1
        return prev;
      },
      { replace: true },
    );
  };

  // Reseta a página quando o filtro muda (inclui navegar p/ a rota nua pelo menu, que limpa os filtros
  // sem passar por writeFilters). A chave IGNORA `page` — senão paginar dispararia o próprio reset — e o
  // reset pula o mount, então voltar de um detalhe preserva o `?page=` restaurado da URL.
  const filterKey = useMemo(() => {
    const p = new URLSearchParams(params);
    p.delete('page');
    return p.toString();
  }, [params]);
  useResetPageOnChange(setPage, [companyId, stage, filterKey]);

  // Busca é global: enquanto há texto, ignora o status do estágio (acha o pedido em qualquer ponto do fluxo).
  // Caso contrário, o estágio ativo fixa o status; "Encerradas" usa os status escolhidos (todos quando nenhum).
  const searching = filters.search.trim().length > 0;
  const statuses: RequisitionStatus[] | undefined = searching
    ? undefined
    : stage === 'encerradas'
      ? filters.statuses.length > 0
        ? filters.statuses
        : CLOSED_STATUSES
      : [STAGE_STATUS[stage]];

  const query: RequisitionsParams = {
    companyId,
    statuses,
    unitId: filters.unitId ?? undefined,
    costCenterId: filters.costCenterId ?? undefined,
    type: filters.type || undefined,
    competencyFrom: filters.competencyFrom || undefined,
    competencyTo: filters.competencyTo || undefined,
    search: filters.search.trim() || undefined,
    minValue: numOrUndef(filters.minValue),
    maxValue: numOrUndef(filters.maxValue),
    sort: filters.sort || undefined,
    page,
    mine,
  };
  const list = useRequisitions(query, { enabled: !!companyId });
  const data = list.data;

  const approveTriage = useApproveTriage();
  const approve = useApproveRequisition();
  const onError = (e: unknown) => toast.error(getErrorMessage(e));

  // A busca global traz linhas de QUALQUER status para a aba — o botão de transição só renderiza
  // quando a linha está de fato no status da aba (senão convida um 409 garantido).
  let extraColumn: { header: string; cell: (r: Requisition) => ReactNode } | undefined;
  if (stage === 'triagem' && can(P.requisitionsTriage)) {
    extraColumn = {
      header: t('requisitions.columns.action'),
      cell: (r: Requisition) =>
        r.status === 'Triage' ? (
          <Button
            variant='brand'
            icon={<Check />}
            onClick={() =>
              approveTriage.mutate(
                { id: r.id },
                { onError, onSuccess: (updated) => toast.success(t(triageApprovedToastKey(updated.status))) },
              )
            }
          >
            {t('requisitions.actions.approveTriage')}
          </Button>
        ) : null,
    };
  } else if (stage === 'aprovacao' && can(P.requisitionsApprove)) {
    extraColumn = {
      header: t('requisitions.columns.action'),
      cell: (r: Requisition) =>
        r.status === 'Approval' ? (
          <Button
            className='btn-approve'
            icon={<Check />}
            onClick={() =>
              approve.mutate(
                { id: r.id },
                { onError, onSuccess: () => toast.success(t('requisitions.actions.approved')) },
              )
            }
          >
            {t('requisitions.actions.approve')}
          </Button>
        ) : null,
    };
  }

  // Toggle asc→desc on repeat; a different column starts ascending.
  const onSort = (field: string) => writeFilters({ ...filters, sort: filters.sort === field ? `-${field}` : field });

  return (
    <>
      <RequisitionFilterBar
        companyId={companyId}
        value={filters}
        onChange={writeFilters}
        statusOptions={stage === 'encerradas' ? CLOSED_STATUSES : undefined}
      />
      {searching && (
        <div className='help rq-filters-hint' role='status'>
          {t('requisitions.filters.searchAllHint')}
        </div>
      )}
      <RequisitionTable
        companyId={companyId}
        title={t(`requisitions.stages.${stage}`)}
        items={data?.items ?? []}
        total={data?.total ?? 0}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        sort={filters.sort}
        onSort={onSort}
        extraColumn={extraColumn}
        loading={list.isLoading}
      />
    </>
  );
}
