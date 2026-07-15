import type { components } from './api/schema';
import { useMe } from './queries';

export type Me = components['schemas']['MeResponse'];

/**
 * Catálogo de permissões — espelha o backend
 * (`Ulbra.One.Common.Application.Authentication.Permissions`). Formato `recurso:ação`.
 * Fonte da verdade da autorização é o servidor; aqui é só para renderizar por cargo.
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
  reportsRead: 'reports:read',
  adminUsers: 'admin:users',
  auditRead: 'audit:read',
} as const;

export type Permission = (typeof P)[keyof typeof P];

/** Admin faz bypass; caso contrário checa a lista vinda de `/auth/me`. */
export function can(me: Me | null | undefined, permission: Permission): boolean {
  if (!me) return false;
  if (me.isAdmin) return true;
  return me.permissions?.includes(permission) ?? false;
}

/** True se o usuário tem QUALQUER uma das permissões (ou a lista está vazia = sem exigência). */
export function canAny(me: Me | null | undefined, permissions: readonly Permission[]): boolean {
  if (permissions.length === 0) return true;
  return permissions.some((permission) => can(me, permission));
}

/** Hook de conveniência ligado ao `useMe()`. */
export function usePermissions() {
  const me = useMe().data ?? null;
  return {
    me,
    isAdmin: me?.isAdmin ?? false,
    can: (permission: Permission) => can(me, permission),
    canAny: (permissions: readonly Permission[]) => canAny(me, permissions),
  };
}
