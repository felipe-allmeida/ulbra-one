import type { ReactNode } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { auth } from './api';
import { usePermissions, type Permission } from './permissions';

/**
 * Guarda de rota por permissão — defesa em profundidade (a segurança real é o servidor).
 * Se o usuário não tiver nenhuma das permissões, redireciona para a home em vez de
 * renderizar uma tela sem dados. Admin passa por bypass (ver `usePermissions`).
 */
export function RequirePermission({ anyOf, children }: { anyOf: Permission[]; children: ReactNode }) {
  const { me, canAny } = usePermissions();
  if (!me) return null; // /auth/me ainda carregando
  if (!canAny(anyOf)) return <Navigate to='/app/home' replace />;
  return <>{children}</>;
}

/**
 * Usuário autenticado mas sem cargo (nenhuma permissão e não-admin) cai numa tela
 * "acesso sendo configurado" em vez de um app vazio com menu fantasma e 403 espalhado.
 */
export function RoleGate({ children }: { children: ReactNode }) {
  const { me, isAdmin } = usePermissions();
  if (!me) return null;
  const hasCargo = isAdmin || (me.permissions?.length ?? 0) > 0;
  return hasCargo ? <>{children}</> : <NoRoleScreen email={me.email} />;
}

function NoRoleScreen({ email }: { email: string | null }) {
  const nav = useNavigate();
  const logout = () => {
    auth.clear();
    nav('/');
  };
  return (
    <div
      style={{
        minHeight: '100dvh',
        display: 'grid',
        placeItems: 'center',
        padding: 24,
        background: 'var(--background)',
        color: 'var(--foreground)',
      }}
    >
      <div style={{ maxWidth: 440, textAlign: 'center', display: 'flex', flexDirection: 'column', gap: 12 }}>
        <div style={{ fontSize: 40, lineHeight: 1 }} aria-hidden='true'>
          🪪
        </div>
        <h1 style={{ margin: 0, fontSize: '1.4rem', fontWeight: 700 }}>Seu acesso está sendo configurado</h1>
        <p style={{ margin: 0, color: 'var(--muted-foreground)', lineHeight: 1.6 }}>
          {email ? (
            <>
              A conta <b>{email}</b> ainda não tem um cargo atribuído.
            </>
          ) : (
            'Sua conta ainda não tem um cargo atribuído.'
          )}{' '}
          Peça a um administrador para atribuir seu cargo (Financeiro, Controladoria ou Contábil) e entre novamente.
        </p>
        <button
          type='button'
          onClick={logout}
          style={{
            marginTop: 8,
            alignSelf: 'center',
            padding: '9px 18px',
            borderRadius: 8,
            border: '1px solid var(--border)',
            background: 'transparent',
            color: 'inherit',
            font: 'inherit',
            cursor: 'pointer',
          }}
        >
          Sair
        </button>
      </div>
    </div>
  );
}
