import { createContext, useContext, useState, type ReactNode } from 'react';
import { useMe, useRoles } from './queries';

// Admin-only "Ver como" (view as). When an Admin picks a role, the app RENDERS as that role
// (nav / landing / role-gated UI) via useEffectiveMe. Server-side authorization is unchanged:
// every request is still made as the real Admin (bypass), so this is a preview, not a grant.
// Non-admins never see the selector and this context stays inert for them.
//
// The provider resolves the picked role's permission set (so useEffectiveMe stays a plain context
// read and does NOT add a data hook to every usePermissions caller — that would break tests that
// mock the queries module partially). Roles are fetched only while an admin is impersonating.

const IMPERSONATION_KEY = 'ulbra-one.viewAs';

interface ImpersonationContextValue {
  asRole: string | null;
  asPermissions: string[];
  setAsRole: (role: string | null) => void;
}

const ImpersonationContext = createContext<ImpersonationContextValue | null>(null);

export function ImpersonationProvider({ children }: { children: ReactNode }) {
  const [asRole, setAsRoleState] = useState<string | null>(() => {
    try {
      return window.localStorage.getItem(IMPERSONATION_KEY) || null;
    } catch {
      return null;
    }
  });

  const realIsAdmin = useMe().data?.isAdmin ?? false;
  const roles = useRoles({ enabled: realIsAdmin && !!asRole });
  const asPermissions = asRole ? (roles.data?.find((r) => r.name === asRole)?.permissions ?? []) : [];

  const setAsRole = (role: string | null) => {
    setAsRoleState(role);
    try {
      if (role) window.localStorage.setItem(IMPERSONATION_KEY, role);
      else window.localStorage.removeItem(IMPERSONATION_KEY);
    } catch {
      // Storage disabled: impersonation still applies for this session.
    }
  };

  return (
    <ImpersonationContext.Provider value={{ asRole, asPermissions, setAsRole }}>
      {children}
    </ImpersonationContext.Provider>
  );
}

// Tolerates absence of the provider (e.g. RoleGate lives outside AppLayout and must use the
// real me): returns no impersonation instead of throwing.
export function useImpersonation(): ImpersonationContextValue {
  const ctx = useContext(ImpersonationContext);
  if (!ctx) return { asRole: null, asPermissions: [], setAsRole: () => {} };
  return ctx;
}
