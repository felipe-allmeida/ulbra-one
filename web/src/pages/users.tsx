import { Fragment, useEffect, useMemo, useState } from 'react';
import { Building2, ChevronDown, ChevronRight, Save, ShieldCheck, UserPlus } from 'lucide-react';
import { type AdminUser, type UnitScopeAssignment, type CostCenterScopeAssignment } from '../api';
import { APPROVER_ROLE, CONTROLLERSHIP_ROLE, isScopedRole, roleLabel } from '../permissions';
import {
  useUsers,
  useRoles,
  useCompanies,
  useAllUnits,
  useAllCostCenters,
  useMe,
  useSetUserRole,
  useSetUserScope,
  useSetUserActive,
  useSetBudgetAdjustmentApprover,
} from '../queries';
import { RegisterUserDialog } from './users-register-dialog';
import { ScopeGroup, pairKey, parseCcKey, parseUnitKey } from './users-scope';
import { PageHeader, Panel, Button, Chip, EmptyState, Select, LoadingState } from '../ui';
import { useToast } from '../toast';
import { getErrorMessage } from '../apiError';

/**
 * Users & access administration (AEL-883, iteration 2). One row per user in a table: role, active flag and a
 * summary of the unified intersection scope (Empresa × Unidade × Centro de custo). The scope is a set of
 * EXPLICIT lists — there is no "empty = all", an empty list means none — and is only relevant for the
 * Requester/Approver roles; every other role (incl. Admin) sees everything ("Vê tudo") and has no editor.
 * Role and scope take effect on the user's next login; the Approver's approval alçada is live. The active
 * flag saves immediately on toggle.
 */

const unitPairKey = (p: UnitScopeAssignment) => pairKey(p.companyId, p.unitId);
const ccPairKey = (p: CostCenterScopeAssignment) => pairKey(p.companyId, p.costCenterId);

/** True when the set has exactly the same members as the array (order-independent). */
const sameMembers = (set: Set<string>, values: readonly string[]) =>
  set.size === values.length && values.every((v) => set.has(v));

/** One-cell summary for the unidades/CC columns. */
const countSummary = (size: number, singular: string, plural: string) =>
  size === 0 ? '—' : `${size} ${size === 1 ? singular : plural}`;

/** Editable draft for one user; sets mirror the server collections. */
interface UserDraft {
  role: string;
  companyIds: Set<string>;
  units: Set<string>;
  costCenters: Set<string>;
}

export function UsersPage() {
  const toast = useToast();
  const usersQ = useUsers();
  const rolesQ = useRoles();
  const companiesQ = useCompanies();
  const unitsQ = useAllUnits();
  const costCentersQ = useAllCostCenters();
  const myUserId = useMe().data?.userId;

  const setRole = useSetUserRole();
  const setScope = useSetUserScope();
  const setActive = useSetUserActive();
  const setApprover = useSetBudgetAdjustmentApprover();

  const users = usersQ.data ?? [];
  const roles = rolesQ.data ?? [];
  const allCompanies = companiesQ.data ?? [];
  const activeCompanies = allCompanies.filter((c) => c.active);
  const allUnits = unitsQ.data ?? [];
  const allCostCenters = costCentersQ.data ?? [];

  const anyPending = setRole.isPending || setScope.isPending;

  const companyNameById = useMemo(() => {
    const map = new Map<string, string>();
    for (const c of companiesQ.data ?? []) map.set(c.id, c.legalName);
    return map;
  }, [companiesQ.data]);

  // Editable role/scope draft per user; initialized once on first load so unsaved edits survive refetches.
  const [draft, setDraft] = useState<Record<string, UserDraft>>({});
  useEffect(() => {
    if (!usersQ.data) return;
    setDraft((prev) => {
      if (Object.keys(prev).length > 0) return prev;
      const next: Record<string, UserDraft> = {};
      for (const u of usersQ.data) {
        next[u.id] = {
          role: u.role,
          companyIds: new Set(u.companyIds),
          units: new Set(u.units.map(unitPairKey)),
          costCenters: new Set(u.costCenters.map(ccPairKey)),
        };
      }
      return next;
    });
  }, [usersQ.data]);

  const [registering, setRegistering] = useState(false);

  // Optimistic-ish override for the active flag (saved on its own toggle); reverted on error.
  const [activeOverride, setActiveOverride] = useState<Record<string, boolean>>({});
  const isActive = (u: AdminUser) => activeOverride[u.id] ?? u.active;
  const toggleActive = (u: AdminUser) => {
    const next = !isActive(u);
    setActiveOverride((prev) => ({ ...prev, [u.id]: next }));
    setActive.mutate(
      { id: u.id, active: next },
      {
        onSuccess: () => toast.success(`${u.name} ${next ? 'ativado' : 'inativado'}.`),
        onError: (e) => {
          setActiveOverride((prev) => ({ ...prev, [u.id]: !next }));
          toast.error(getErrorMessage(e));
        },
      },
    );
  };

  // Per-user "can approve budget adjustment" grant (AEL-883), saved on its own toggle; reverted on error.
  // Only offered to Controladoria users (the role that carries budget-adjustments:approve).
  const [approverOverride, setApproverOverride] = useState<Record<string, boolean>>({});
  const isApprover = (u: AdminUser) => approverOverride[u.id] ?? u.canApproveBudgetAdjustment;
  const toggleApprover = (u: AdminUser) => {
    const next = !isApprover(u);
    setApproverOverride((prev) => ({ ...prev, [u.id]: next }));
    setApprover.mutate(
      { id: u.id, canApprove: next },
      {
        onSuccess: () => toast.success(`${u.name} ${next ? 'pode aprovar' : 'não aprova mais'} Adequação.`),
        onError: (e) => {
          setApproverOverride((prev) => ({ ...prev, [u.id]: !next }));
          toast.error(getErrorMessage(e));
        },
      },
    );
  };

  // Which users have the scope editor open (collapsed by default).
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const toggleExpand = (userId: string) =>
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(userId)) next.delete(userId);
      else next.add(userId);
      return next;
    });

  const patch = (userId: string, fn: (d: UserDraft) => UserDraft) =>
    setDraft((prev) => (prev[userId] ? { ...prev, [userId]: fn(prev[userId]) } : prev));

  const setRoleDraft = (userId: string, role: string) => patch(userId, (d) => ({ ...d, role }));

  const toggleCompany = (userId: string, companyId: string) =>
    patch(userId, (d) => {
      const companyIds = new Set(d.companyIds);
      const units = new Set(d.units);
      const costCenters = new Set(d.costCenters);
      if (companyIds.has(companyId)) {
        companyIds.delete(companyId);
        // Unchecking a company drops its units and cost centers from the draft.
        for (const key of [...units]) if (key.startsWith(`${companyId}|`)) units.delete(key);
        for (const key of [...costCenters]) if (key.startsWith(`${companyId}|`)) costCenters.delete(key);
      } else {
        companyIds.add(companyId);
      }
      return { ...d, companyIds, units, costCenters };
    });

  const toggleUnit = (userId: string, companyId: string, unitId: string) =>
    patch(userId, (d) => {
      const units = new Set(d.units);
      const key = pairKey(companyId, unitId);
      if (units.has(key)) units.delete(key);
      else units.add(key);
      return { ...d, units };
    });

  const toggleCostCenter = (userId: string, companyId: string, costCenterId: string) =>
    patch(userId, (d) => {
      const costCenters = new Set(d.costCenters);
      const key = pairKey(companyId, costCenterId);
      if (costCenters.has(key)) costCenters.delete(key);
      else costCenters.add(key);
      return { ...d, costCenters };
    });

  // "Selecionar todos" stores the explicit snapshot for the group (no "empty = all").
  const selectAllCompanies = (userId: string) =>
    patch(userId, (d) => ({ ...d, companyIds: new Set(activeCompanies.map((c) => c.id)) }));
  const selectAllUnits = (userId: string) =>
    patch(userId, (d) => {
      const units = new Set<string>();
      for (const un of allUnits) if (d.companyIds.has(un.companyId)) units.add(pairKey(un.companyId, un.id));
      return { ...d, units };
    });
  const selectAllCostCenters = (userId: string) =>
    patch(userId, (d) => {
      const costCenters = new Set<string>();
      for (const cc of allCostCenters)
        if (d.companyIds.has(cc.companyId)) costCenters.add(pairKey(cc.companyId, cc.id));
      return { ...d, costCenters };
    });

  const isDirty = (u: AdminUser) => {
    const d = draft[u.id];
    if (!d) return false;
    return (
      d.role !== u.role ||
      !sameMembers(d.companyIds, u.companyIds) ||
      !sameMembers(d.units, u.units.map(unitPairKey)) ||
      !sameMembers(d.costCenters, u.costCenters.map(ccPairKey))
    );
  };

  // Chained mutateAsync (each step runs after the previous): role (if changed) → unified scope.
  const save = async (u: AdminUser) => {
    const d = draft[u.id];
    if (!d) return;
    try {
      if (d.role !== u.role) await setRole.mutateAsync({ id: u.id, role: d.role });
      await setScope.mutateAsync({
        id: u.id,
        companyIds: [...d.companyIds],
        units: [...d.units].map(parseUnitKey),
        costCenters: [...d.costCenters].map(parseCcKey),
      });
      toast.success(`Acessos de ${u.name} salvos.`);
    } catch (e) {
      toast.error(getErrorMessage(e));
    }
  };

  const companySummary = (ids: Set<string>) => {
    if (ids.size === 0) return '—';
    if (ids.size === 1) {
      const [only] = [...ids];
      return companyNameById.get(only) ?? only;
    }
    return `${ids.size} empresas`;
  };

  if (usersQ.isLoading || companiesQ.isLoading || rolesQ.isLoading || unitsQ.isLoading || costCentersQ.isLoading) {
    return <LoadingState />;
  }
  if (activeCompanies.length === 0) {
    return (
      <EmptyState
        icon={<Building2 />}
        title='Nenhuma empresa cadastrada'
        sub='Cadastre empresas em Estrutura antes de planejar acessos por usuário.'
      />
    );
  }
  const registerButton = (
    <Button variant='brand' icon={<UserPlus size={14} />} onClick={() => setRegistering(true)}>
      Cadastrar usuário
    </Button>
  );
  const registerDialog = registering && (
    <RegisterUserDialog
      roles={roles}
      companies={activeCompanies}
      units={allUnits}
      costCenters={allCostCenters}
      onClose={() => setRegistering(false)}
    />
  );

  if (users.length === 0) {
    return (
      <>
        <EmptyState
          icon={<ShieldCheck />}
          title='Nenhum usuário'
          sub='O acesso é por convite: cadastre o e-mail para que a pessoa possa entrar com o Google.'
          action={registerButton}
        />
        {registerDialog}
      </>
    );
  }

  return (
    <>
      <PageHeader
        title='Usuários & acessos'
        sub='Cadastre o e-mail de quem pode entrar e defina o cargo, se está ativo e — para Solicitante e Aprovador — o escopo de dados (empresa × unidade × natureza orçamentária).'
      >
        {registerButton}
      </PageHeader>

      {registerDialog}

      <Panel title='Usuários'>
        <div style={{ overflowX: 'auto' }}>
          <table>
            <thead>
              <tr>
                <th>Usuário</th>
                <th>Cargo</th>
                <th style={{ textAlign: 'center' }}>Ativo</th>
                <th style={{ textAlign: 'center' }}>Aprova Adequação</th>
                <th>Empresa</th>
                <th>Unidades</th>
                <th>CC</th>
                <th className='td-action'></th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => {
                const d = draft[u.id];
                const role = d?.role ?? u.role;
                const scoped = isScopedRole(role);
                const companyIds = d?.companyIds ?? new Set(u.companyIds);
                const units = d?.units ?? new Set(u.units.map(unitPairKey));
                const costCenters = d?.costCenters ?? new Set(u.costCenters.map(ccPairKey));
                const isOpen = expanded.has(u.id);
                const grantedCompanies = activeCompanies.filter((c) => companyIds.has(c.id));
                const seesAll = <span className='help'>Vê tudo</span>;
                return (
                  <Fragment key={u.id}>
                    <tr>
                      <td>
                        <div style={{ fontWeight: 600 }}>{u.name || '—'}</div>
                        <span className='help t-mono'>{u.email}</span>
                        {u.pendingFirstLogin && (
                          <div>
                            <Chip tone='warn'>Aguardando 1º acesso</Chip>
                          </div>
                        )}
                      </td>
                      <td>
                        <Select
                          aria-label={`Cargo de ${u.name}`}
                          value={role}
                          onChange={(e) => setRoleDraft(u.id, e.target.value)}
                        >
                          {roles.map((r) => (
                            <option key={r.id} value={r.name}>
                              {roleLabel(r.name)}
                            </option>
                          ))}
                        </Select>
                      </td>
                      <td style={{ textAlign: 'center' }}>
                        <input
                          type='checkbox'
                          aria-label={`Ativo: ${u.name}`}
                          checked={isActive(u)}
                          disabled={setActive.isPending || u.id === myUserId}
                          onChange={() => toggleActive(u)}
                        />
                      </td>
                      <td style={{ textAlign: 'center' }}>
                        {u.role === CONTROLLERSHIP_ROLE ? (
                          <input
                            type='checkbox'
                            aria-label={`Aprova Adequação: ${u.name}`}
                            checked={isApprover(u)}
                            disabled={setApprover.isPending}
                            onChange={() => toggleApprover(u)}
                          />
                        ) : (
                          <span className='help'>—</span>
                        )}
                      </td>
                      <td>{scoped ? companySummary(companyIds) : seesAll}</td>
                      <td>{scoped ? countSummary(units.size, 'unidade', 'unidades') : seesAll}</td>
                      <td>{scoped ? countSummary(costCenters.size, 'natureza', 'naturezas') : seesAll}</td>
                      <td className='td-action'>
                        <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', alignItems: 'center' }}>
                          {isDirty(u) && (
                            <Button icon={<Save size={14} />} disabled={anyPending} onClick={() => void save(u)}>
                              Salvar
                            </Button>
                          )}
                          {scoped && (
                            <button
                              type='button'
                              className='icon-btn'
                              aria-expanded={isOpen}
                              aria-label={`Editar acessos de ${u.name}`}
                              onClick={() => toggleExpand(u.id)}
                            >
                              {isOpen ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>

                    {scoped && isOpen && d && (
                      <tr>
                        <td colSpan={8} style={{ padding: 16 }}>
                          <div style={{ display: 'grid', gap: 20 }}>
                            <p className='help' style={{ margin: 0 }}>
                              Sem regra de vazio: para liberar tudo use “Selecionar todos”. Vazio = nada.
                              {role === APPROVER_ROLE &&
                                ' Para o Aprovador, este mesmo escopo governa o que ele vê e o que pode aprovar.'}
                            </p>

                            <ScopeGroup
                              title='Empresas'
                              onSelectAll={() => selectAllCompanies(u.id)}
                              selectAllLabel={`Selecionar todas as empresas para ${u.name}`}
                            >
                              <div style={{ display: 'grid', gap: 6 }}>
                                {activeCompanies.map((c) => (
                                  <label
                                    key={c.id}
                                    style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 500 }}
                                  >
                                    <input
                                      type='checkbox'
                                      aria-label={`Empresa ${c.legalName} para ${u.name}`}
                                      checked={companyIds.has(c.id)}
                                      onChange={() => toggleCompany(u.id, c.id)}
                                    />
                                    {c.legalName}
                                  </label>
                                ))}
                              </div>
                            </ScopeGroup>

                            <ScopeGroup
                              title='Unidades'
                              onSelectAll={() => selectAllUnits(u.id)}
                              selectAllLabel={`Selecionar todas as unidades para ${u.name}`}
                            >
                              {grantedCompanies.length === 0 ? (
                                <span className='help'>Marque ao menos uma empresa para liberar unidades.</span>
                              ) : (
                                <div style={{ display: 'grid', gap: 12 }}>
                                  {grantedCompanies.map((c) => {
                                    const companyUnits = allUnits.filter((x) => x.companyId === c.id);
                                    return (
                                      <div key={c.id}>
                                        <div className='help' style={{ marginBottom: 4 }}>
                                          {c.legalName}
                                        </div>
                                        <div style={{ display: 'grid', gap: 6, paddingLeft: 4 }}>
                                          {companyUnits.length === 0 ? (
                                            <span className='help'>Nenhuma unidade nesta empresa.</span>
                                          ) : (
                                            companyUnits.map((un) => (
                                              <label
                                                key={un.id}
                                                style={{ display: 'flex', alignItems: 'center', gap: 8 }}
                                              >
                                                <input
                                                  type='checkbox'
                                                  aria-label={`Unidade ${un.name} para ${u.name}`}
                                                  checked={units.has(pairKey(c.id, un.id))}
                                                  onChange={() => toggleUnit(u.id, c.id, un.id)}
                                                />
                                                <span>
                                                  {un.name}
                                                  {un.acronym && (
                                                    <span className='help t-mono' style={{ marginLeft: 6 }}>
                                                      {un.acronym}
                                                    </span>
                                                  )}
                                                </span>
                                              </label>
                                            ))
                                          )}
                                        </div>
                                      </div>
                                    );
                                  })}
                                </div>
                              )}
                            </ScopeGroup>

                            <ScopeGroup
                              title='Naturezas orçamentárias'
                              onSelectAll={() => selectAllCostCenters(u.id)}
                              selectAllLabel={`Selecionar todas as naturezas orçamentárias para ${u.name}`}
                            >
                              {grantedCompanies.length === 0 ? (
                                <span className='help'>
                                  Marque ao menos uma empresa para liberar naturezas orçamentárias.
                                </span>
                              ) : (
                                <div style={{ display: 'grid', gap: 12 }}>
                                  {grantedCompanies.map((c) => {
                                    const companyCcs = allCostCenters.filter((x) => x.companyId === c.id);
                                    return (
                                      <div key={c.id}>
                                        <div className='help' style={{ marginBottom: 4 }}>
                                          {c.legalName}
                                        </div>
                                        <div style={{ display: 'grid', gap: 6, paddingLeft: 4 }}>
                                          {companyCcs.length === 0 ? (
                                            <span className='help'>Nenhuma natureza orçamentária nesta empresa.</span>
                                          ) : (
                                            companyCcs.map((cc) => (
                                              <label
                                                key={cc.id}
                                                style={{ display: 'flex', alignItems: 'center', gap: 8 }}
                                              >
                                                <input
                                                  type='checkbox'
                                                  aria-label={`Natureza orçamentária ${cc.name} para ${u.name}`}
                                                  checked={costCenters.has(pairKey(c.id, cc.id))}
                                                  onChange={() => toggleCostCenter(u.id, c.id, cc.id)}
                                                />
                                                <span>
                                                  {cc.code} · {cc.name}
                                                </span>
                                              </label>
                                            ))
                                          )}
                                        </div>
                                      </div>
                                    );
                                  })}
                                </div>
                              )}
                            </ScopeGroup>

                            <p className='help' style={{ margin: 0 }}>
                              Cargo e escopo passam a valer no próximo login do usuário. Para o Aprovador, a alçada de
                              aprovação vale imediatamente.
                            </p>
                          </div>
                        </td>
                      </tr>
                    )}
                  </Fragment>
                );
              })}
            </tbody>
          </table>
        </div>
      </Panel>

      <p className='help' style={{ marginTop: 12, display: 'flex', alignItems: 'center', gap: 6 }}>
        <ShieldCheck size={14} /> O escopo (empresa × unidade × natureza orçamentária) vale apenas para{' '}
        <strong>Solicitante</strong> e <strong>Aprovador</strong>; os demais cargos enxergam tudo. Sem regra de vazio:
        lista vazia = nenhum acesso.
      </p>
    </>
  );
}
