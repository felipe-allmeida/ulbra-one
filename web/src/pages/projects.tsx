import { useState } from 'react';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { Controller, useForm } from 'react-hook-form';
import { AlertTriangle, ChevronLeft, FolderKanban, Pencil, Plus, Trash2 } from 'lucide-react';
import { useApp } from '../store';
import { useDeleteProject, useProject, useProjectMetrics, useProjects, useSaveProject, useUnits } from '../queries';
import { brl, moneyInput, parseMoneyInput } from '../lib';
import { useBackNavigation, useOriginNav } from '../nav';
import {
  Button,
  Chip,
  ConfirmDialog,
  CurrencyInput,
  EmptyState,
  Field,
  Input,
  Kpi,
  LoadingState,
  PageHeader,
  Panel,
  Select,
  Skeleton,
} from '../ui';
import { P, usePermissions } from '../permissions';
import { fieldError, projectSchema, resolver, type ProjectValues } from './schemas';
import { getErrorMessage } from '../apiError';
import { clampPct } from './requisitions/budget-display';
import type { Project, ProjectStatus } from '../api';

const STATUS_TONE: Record<ProjectStatus, 'default' | 'brand'> = {
  Active: 'brand',
  Finished: 'default',
  Cancelled: 'default',
};

const STATUS_LABEL: Record<ProjectStatus, string> = {
  Active: 'Ativo',
  Finished: 'Finalizado',
  Cancelled: 'Cancelado',
};

// Re-exported so existing importers of `./projects` keep working; single source of truth is budget-display.
export { clampPct };

/* ===================== LIST ===================== */
export function ProjectsListPage() {
  const { empresa, empresaId, empresas, isAllCompanies, scopeReady } = useApp();
  const nav = useNavigate();
  const openDetail = useOriginNav();
  // Consolidated "Todas as empresas" (AEL-906): read-only overview across every company; hide create.
  const canWrite = usePermissions().can(P.projectsWrite) && !isAllCompanies;
  const projectsQuery = useProjects(empresaId);
  const projects = projectsQuery.data ?? [];
  const nomeEmpresa = (id: string) => empresas.find((e) => e.id === id)?.legalName ?? '—';

  const tetoTotal = projects.reduce((s, p) => s + p.budgetLimit, 0);
  const ativos = projects.filter((p) => p.status === 'Active').length;

  return (
    <>
      <PageHeader
        title='Projetos'
        sub='Projetos com teto de gasto acumulado. As requisições vinculadas consomem o teto ao longo de toda a vida do projeto — não por competência.'
      >
        {canWrite && (
          <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => nav('/app/projects/new')}>
            Novo projeto
          </Button>
        )}
      </PageHeader>

      {!scopeReady ? (
        <EmptyState
          icon={<FolderKanban />}
          title='Nenhuma empresa cadastrada'
          sub='Cadastre uma empresa em Estrutura para começar.'
        />
      ) : projectsQuery.isLoading ? (
        <div className='cc-grid'>
          {Array.from({ length: 4 }).map((_, i) => (
            <div className='cc-card' key={i}>
              <div className='cc-card-top'>
                <div>
                  <Skeleton width={150} />
                  <div style={{ marginTop: 8 }}>
                    <Skeleton width={90} height={10} />
                  </div>
                </div>
                <Skeleton width={70} />
              </div>
            </div>
          ))}
        </div>
      ) : projects.length === 0 ? (
        <EmptyState
          icon={<FolderKanban />}
          title='Nenhum projeto'
          sub={
            isAllCompanies ? 'Nenhum projeto cadastrado.' : 'Crie o primeiro projeto com teto de gasto desta empresa.'
          }
        />
      ) : (
        <>
          <div className='kpi-grid'>
            <Kpi label='Projetos' value={String(projects.length)} foot={`${ativos} ativo(s)`} />
            <Kpi label='Teto total' value={brl(tetoTotal)} foot={`${projects.length} projeto(s)`} />
          </div>

          <div className='cc-grid'>
            {projects.map((p) => (
              <div className='cc-card' key={p.id} onClick={() => openDetail(`/app/projects/${p.id}`)} title='Abrir'>
                <div className='cc-card-top'>
                  <div>
                    <div className='cc-name'>{p.name}</div>
                    <div className='cc-code'>{p.description ?? '—'}</div>
                  </div>
                  <div className='cc-orc'>
                    <div className='v'>{brl(p.budgetLimit)}</div>
                    <div className='l'>teto de gasto</div>
                  </div>
                </div>
                <div className='chips'>
                  {isAllCompanies && <Chip tone='brand'>{nomeEmpresa(p.companyId)}</Chip>}
                  <Chip tone={STATUS_TONE[p.status]}>{STATUS_LABEL[p.status]}</Chip>
                  {p.initialConsumedValue > 0 && <Chip>consumo anterior {brl(p.initialConsumedValue)}</Chip>}
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </>
  );
}

/* ===================== DETAIL ===================== */
export function ProjectDetailPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const location = useLocation();
  const { goBack, label: backLabel } = useBackNavigation('/app/projects');
  const canWrite = usePermissions().can(P.projectsWrite);
  const projectQuery = useProject(id);
  const project = projectQuery.data;
  const metrics = useProjectMetrics(id).data;
  const [confirmDelete, setConfirmDelete] = useState(false);
  const del = useDeleteProject({ onSuccess: () => nav('/app/projects') });

  if (projectQuery.isLoading) return <LoadingState />;
  if (!project) return null;

  return (
    <>
      <PageHeader title={project.name} sub={project.description ?? 'Acompanhamento do teto de gasto acumulado.'}>
        <Button icon={<ChevronLeft />} onClick={goBack}>
          {backLabel}
        </Button>
        {canWrite && (
          <>
            <Button
              icon={<Pencil />}
              onClick={() => nav(`/app/projects/${project.id}/edit`, { state: location.state })}
            >
              Editar
            </Button>
            <Button icon={<Trash2 />} onClick={() => setConfirmDelete(true)}>
              Excluir
            </Button>
          </>
        )}
      </PageHeader>

      {metrics && (
        <>
          <div className='kpi-grid'>
            <Kpi label='Orçado (teto)' value={brl(metrics.budgetLimit)} />
            <Kpi label='Previsto' value={brl(metrics.previsto)} foot={`${metrics.percentPrevisto}% do teto`} />
            <Kpi label='Realizado' value={brl(metrics.realizado)} foot={`${metrics.percentRealizado}% do teto`} />
            <Kpi
              label='Saldo (teto − previsto)'
              value={brl(metrics.saldo)}
              foot={
                metrics.isOverCeiling
                  ? `⚠ teto estourado · ${metrics.requisitionCount} requisição(ões)`
                  : `${metrics.requisitionCount} requisição(ões)`
              }
            />
          </div>

          <Panel title='Uso do orçamento' padded>
            <div className='proj-bar-legend'>
              <span>
                <i className='dot dot-realizado' /> Realizado
              </span>
              <span>
                <i className='dot dot-previsto' /> Previsto (contratado)
              </span>
              <span className='proj-bar-pct'>{metrics.percentPrevisto}%</span>
            </div>
            {/* Stacked: realizado (solid) inside previsto (lighter) — mirrors the legacy tracking bar. */}
            <div className='cc-progress proj-progress'>
              <div
                className='cc-progress-bar proj-bar-previsto'
                style={{ width: `${clampPct(metrics.percentPrevisto)}%` }}
              />
              <div
                className='cc-progress-bar proj-bar-realizado'
                style={{ width: `${clampPct(metrics.percentRealizado)}%` }}
              />
            </div>

            {metrics.isOverCeiling && (
              <div className='proj-alert' role='alert'>
                <AlertTriangle size={16} />
                <span>
                  Atenção: o total previsto ({brl(metrics.previsto)}) extrapola o teto do projeto (
                  {brl(metrics.budgetLimit)}) em {brl(Math.abs(metrics.saldo))}.
                </span>
              </div>
            )}
          </Panel>

          <Panel title='Detalhes' padded>
            <div className='chips'>
              <Chip tone={STATUS_TONE[project.status]}>{STATUS_LABEL[project.status]}</Chip>
              {project.initialConsumedValue > 0 && (
                <Chip>consumo anterior ao sistema: {brl(project.initialConsumedValue)}</Chip>
              )}
            </div>
          </Panel>
        </>
      )}

      {confirmDelete && (
        <ConfirmDialog
          title='Excluir projeto'
          message={`Excluir "${project.name}"? Projetos com requisições vinculadas não podem ser excluídos — cancele-o em vez disso.`}
          confirmLabel='Excluir'
          danger
          busy={del.isPending}
          onConfirm={() => del.mutate(project.id)}
          onCancel={() => setConfirmDelete(false)}
        />
      )}
    </>
  );
}

/* ===================== FORM ===================== */
export function ProjectFormPage() {
  const { id } = useParams();
  const nav = useNavigate();
  const { goBack } = useBackNavigation('/app/projects');
  const { empresa, empresaId } = useApp();
  const unidades = useUnits(empresaId).data ?? [];
  const existingQuery = useProject(id);
  const existing = existingQuery.data;
  const editing = !!id;

  if (editing && existingQuery.isLoading) return <LoadingState />;
  if (editing && !existing) return null;

  return (
    <>
      <PageHeader title={editing ? 'Editar projeto' : 'Novo projeto'}>
        <Button icon={<ChevronLeft />} onClick={goBack}>
          Voltar
        </Button>
      </PageHeader>
      <Panel title='Dados do projeto' padded>
        {empresa && (
          <ProjectForm
            unidades={unidades}
            initial={existing}
            onSaved={(p) => nav(`/app/projects/${p.id}`)}
            onCancel={() => nav('/app/projects')}
          />
        )}
      </Panel>
    </>
  );
}

function ProjectForm({
  unidades,
  initial,
  onSaved,
  onCancel,
}: {
  unidades: { id: string; acronym: string; name: string }[];
  initial?: Project;
  onSaved: (p: Project) => void;
  onCancel: () => void;
}) {
  const { control, handleSubmit, formState } = useForm<ProjectValues>({
    resolver: resolver(projectSchema),
    mode: 'onChange',
    defaultValues: {
      name: initial?.name ?? '',
      unitId: initial?.unitId ?? '',
      description: initial?.description ?? '',
      budgetLimit: initial ? moneyInput(initial.budgetLimit) : '',
      initialConsumedValue: initial ? moneyInput(initial.initialConsumedValue) : '',
    },
  });

  const mutation = useSaveProject({ onSuccess: onSaved });
  const erro = mutation.error ? getErrorMessage(mutation.error) : null;

  const onValid = (v: ProjectValues) =>
    mutation.mutate({
      id: initial?.id,
      unitId: v.unitId,
      name: v.name.trim(),
      description: v.description?.trim() || null,
      budgetLimit: parseMoneyInput(v.budgetLimit) ?? 0,
      initialConsumedValue: parseMoneyInput(v.initialConsumedValue) ?? 0,
    });

  return (
    <form onSubmit={(e) => void handleSubmit(onValid)(e)} noValidate>
      <div className='edit-grid'>
        <Field label='Nome'>
          <Controller
            control={control}
            name='name'
            render={({ field }) => <Input autoFocus {...field} placeholder='ex.: Obra Bloco A' />}
          />
          {fieldError(formState.errors.name?.message) && (
            <div className='field-err'>{fieldError(formState.errors.name?.message)}</div>
          )}
        </Field>

        <Field label='Unidade'>
          <Controller
            control={control}
            name='unitId'
            render={({ field }) => (
              <Select {...field} disabled={!!initial}>
                <option value=''>Selecione…</option>
                {unidades.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.acronym} — {u.name}
                  </option>
                ))}
              </Select>
            )}
          />
          {fieldError(formState.errors.unitId?.message) && (
            <div className='field-err'>{fieldError(formState.errors.unitId?.message)}</div>
          )}
        </Field>

        <Field label='Teto de gasto (R$)' help='Limite acumulado para toda a vida do projeto.'>
          <Controller
            control={control}
            name='budgetLimit'
            render={({ field }) => (
              <CurrencyInput
                value={field.value}
                onValueChange={field.onChange}
                onBlur={field.onBlur}
                name={field.name}
                placeholder='ex.: 500.000,00'
              />
            )}
          />
          {fieldError(formState.errors.budgetLimit?.message) && (
            <div className='field-err'>{fieldError(formState.errors.budgetLimit?.message)}</div>
          )}
        </Field>

        <Field
          label='Consumo anterior ao sistema (R$)'
          help='Quanto já havia sido gasto antes da implantação. Conta no previsto e no realizado.'
        >
          <Controller
            control={control}
            name='initialConsumedValue'
            render={({ field }) => (
              <CurrencyInput
                value={field.value}
                onValueChange={field.onChange}
                onBlur={field.onBlur}
                name={field.name}
                placeholder='0,00'
              />
            )}
          />
          {fieldError(formState.errors.initialConsumedValue?.message) && (
            <div className='field-err'>{fieldError(formState.errors.initialConsumedValue?.message)}</div>
          )}
        </Field>

        <Field label='Descrição'>
          <Controller
            control={control}
            name='description'
            render={({ field }) => <Input {...field} placeholder='opcional' />}
          />
        </Field>
      </div>

      {erro && <div className='field-err'>{erro}</div>}

      <div className='form-actions'>
        <Button type='button' onClick={onCancel}>
          Cancelar
        </Button>
        <Button type='submit' variant='brand' disabled={mutation.isPending}>
          {mutation.isPending ? 'Salvando…' : 'Salvar'}
        </Button>
      </div>
    </form>
  );
}
