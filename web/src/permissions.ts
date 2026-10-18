import type { components } from './api/schema';
import { useMe } from './queries';
import { useImpersonation } from './impersonation';

/** `/auth/me`. */
export type Me = components['schemas']['MeResponse'];

/**
 * Permission catalog — mirrors the backend
 * (`Ulbra.One.Common.Application.Authentication.Permissions`). Format `resource:action`.
 * The source of truth for authorization is the server; here it is only for rendering by role.
 */
export const P = {
  coreRead: 'core:read',
  coreWrite: 'core:write',
  registryRead: 'registry:read',
  registryWrite: 'registry:write',
  payablesRead: 'payables:read',
  payablesWrite: 'payables:write',
  payablesApprove: 'payables:approve',
  payablesPay: 'payables:pay',
  payablesTax: 'payables:tax',
  receivablesRead: 'receivables:read',
  receivablesWrite: 'receivables:write',
  receivablesReceive: 'receivables:receive',
  treasuryRead: 'treasury:read',
  treasuryWrite: 'treasury:write',
  treasuryApprove: 'treasury:approve',
  accountingRead: 'accounting:read',
  accountingWrite: 'accounting:write',
  accountingPost: 'accounting:post',
  fiscalRead: 'fiscal:read',
  fiscalWrite: 'fiscal:write',
  projectsRead: 'projects:read',
  projectsWrite: 'projects:write',
  requisitionsRead: 'requisitions:read',
  requisitionsWrite: 'requisitions:write',
  requisitionsTriage: 'requisitions:triage',
  requisitionsApprove: 'requisitions:approve',
  requisitionsSlaReport: 'requisitions:sla-report',
  budgetAdjustmentsRead: 'budget-adjustments:read',
  budgetAdjustmentsWrite: 'budget-adjustments:write',
  budgetAdjustmentsTriage: 'budget-adjustments:triage',
  budgetAdjustmentsApprove: 'budget-adjustments:approve',
  reportsRead: 'reports:read',
  adminUsers: 'admin:users',
  auditRead: 'audit:read',
} as const;

export type Permission = (typeof P)[keyof typeof P];

/**
 * Portuguese labels for the roles. The backend stores the technical name in English
 * (`User.Role` references the role by name — it is an identifier, can't be renamed there),
 * so the translation is display-only. Single source consumed by the access matrix and the
 * menu header.
 */
/** Approver role name (AEL-883). Single source of the string in the frontend:
 * used in ROLE_LABEL and to gate the scope editor on the users screen. */
export const APPROVER_ROLE = 'Approver';
/** Requester role name (AEL-883). Single source of the string in the frontend. */
export const REQUESTER_ROLE = 'Requester';
/** Controllership role name. The role that carries budget-adjustments:approve — so the "pode aprovar
 * Adequação" per-user toggle is offered only to these users on the admin screen (AEL-883). */
export const CONTROLLERSHIP_ROLE = 'Controllership';
/** Presidency role name (AEL-883): does almost everything Admin does except user config, and — like Admin —
 * sees all companies (bypasses scope). Used to widen the company switcher. */
export const PRESIDENCY_ROLE = 'Presidency';
/** Finance role name. Single source of the string in the frontend. */
export const FINANCE_ROLE = 'Finance';
/** Accounting role name. Single source of the string in the frontend. */
export const ACCOUNTING_ROLE = 'Accounting';
/** Tax (Tributário) role name. Single source of the string in the frontend. */
export const TAX_ROLE = 'Tax';

/** Management roles with the consolidated "Todas as empresas" view (AEL-906): Admin, Presidency, Controllership,
 * Finance, Accounting. Drives the company switcher + the "Todas" option (Requester/Approver/User stay single). */
export function canSeeConsolidated(me: Me | null | undefined): boolean {
  return (
    !!me &&
    (me.isAdmin || [PRESIDENCY_ROLE, CONTROLLERSHIP_ROLE, FINANCE_ROLE, ACCOUNTING_ROLE].includes(me.role ?? ''))
  );
}

/**
 * The roles whose data + approval actions are restricted by the unified intersection scope
 * (Empresa × Unidade × Centro de custo). Every other role sees everything. Consumed by the users
 * screen to decide whether to show the scope editor.
 */
export function isScopedRole(role: string): boolean {
  return role === REQUESTER_ROLE || role === APPROVER_ROLE;
}

/** Roles allowed to see the Finance-specific manual (Remessa/Retorno CNAB): Controladoria, Financeiro
 * e Presidência (+ Admin via bypass). Exclui Contábil, Solicitante, Aprovador e Usuário. Consumido
 * pelo audience `financeManual` do nav e pelo filtro de seções da página de Documentação. */
export function canSeeFinanceManual(me: Me | null | undefined): boolean {
  return !!me && (me.isAdmin || [CONTROLLERSHIP_ROLE, FINANCE_ROLE, PRESIDENCY_ROLE].includes(me.role ?? ''));
}

export const ROLE_LABEL: Record<string, string> = {
  Admin: 'Admin',
  User: 'Usuário',
  Finance: 'Financeiro',
  Controllership: 'Controladoria',
  Accounting: 'Contábil',
  Requester: 'Solicitante',
  [APPROVER_ROLE]: 'Aprovador',
  [PRESIDENCY_ROLE]: 'Presidência',
  [TAX_ROLE]: 'Tributário',
};

/** Role name in Portuguese; falls back to the value itself if no label is mapped. */
export function roleLabel(role: string | null | undefined): string {
  if (!role) return '';
  return ROLE_LABEL[role] ?? role;
}

/** Admin bypasses; otherwise checks the list coming from `/auth/me`. */
export function can(me: Me | null | undefined, permission: Permission): boolean {
  if (!me) return false;
  if (me.isAdmin) return true;
  return me.permissions?.includes(permission) ?? false;
}

/** True if the user has ANY of the permissions (or the list is empty = no requirement). */
export function canAny(me: Me | null | undefined, permissions: readonly Permission[]): boolean {
  if (permissions.length === 0) return true;
  return permissions.some((permission) => can(me, permission));
}

/**
 * Final approver signature (Approver, AEL-883): approves without writing or
 * triaging. Unique in the role catalog (RoleSeed), drives the role's restricted nav/routes. Admin never
 * matches (sees everything); "Ver como Aprovador" matches, since impersonation clears isAdmin and injects the role's permissions.
 */
export function isFinalApprover(me: Me | null | undefined): boolean {
  if (!me || me.isAdmin) return false;
  return can(me, P.requisitionsApprove) && !can(me, P.requisitionsWrite) && !can(me, P.requisitionsTriage);
}

/**
 * Controllership signature: triages without approving (the only role with requisitions:triage in RoleSeed).
 * Mirrors isFinalApprover and is mutually exclusive with it; drives the triage badge/landing. Admin never
 * matches (sees everything); "Ver como Controladoria" matches (impersonation clears isAdmin).
 */
export function isController(me: Me | null | undefined): boolean {
  if (!me || me.isAdmin) return false;
  return can(me, P.requisitionsTriage) && !can(me, P.requisitionsApprove);
}

/**
 * Finance (Finance/Treasury) signature: pays and moves money without approving or triaging —
 * `payables:pay` + `treasury:write` are unique to Finance in RoleSeed (segregation: "pays but doesn't
 * approve"). Drives the Gestão Financeira landing/nav. Admin never matches; "Ver como Financeiro" does.
 */
export function isFinance(me: Me | null | undefined): boolean {
  if (!me || me.isAdmin) return false;
  return (
    can(me, P.payablesPay) && can(me, P.treasuryWrite) && !can(me, P.payablesApprove) && !can(me, P.requisitionsTriage)
  );
}

/**
 * Effective "me" used for RENDERING. Normally the real `/auth/me`. When an Admin uses
 * "Ver como" (impersonation), it returns a synthetic me with the permission set of the chosen
 * role (isAdmin forced to false), so gated nav/landing/UI render as that role.
 * The role list is only fetched while an admin is actually impersonating. Server
 * authorization does not change — requests continue as the real Admin.
 */
export function useEffectiveMe(): Me | null {
  const realMe = useMe().data ?? null;
  const { asRole, asPermissions } = useImpersonation();
  if (realMe?.isAdmin && asRole) {
    return { ...realMe, isAdmin: false, role: asRole, permissions: asPermissions };
  }
  return realMe;
}

/** Convenience hook wired to `useMe()` (with "Ver como" via `useEffectiveMe`). */
export function usePermissions() {
  const query = useMe();
  const me = useEffectiveMe();
  return {
    me,
    isAdmin: me?.isAdmin ?? false,
    finalApprover: isFinalApprover(me),
    controller: isController(me),
    finance: isFinance(me),
    // Exposed so the guards can distinguish "loading" from "could not confirm the session":
    // isError (network/5xx) OR isPaused (React Query paused the fetch after detecting offline — otherwise
    // the guard would stay `null` forever = blank screen). refetch allows the manual retry.
    isError: query.isError,
    isPaused: query.isPaused,
    refetch: query.refetch,
    can: (permission: Permission) => can(me, permission),
    canAny: (permissions: readonly Permission[]) => canAny(me, permissions),
  };
}
