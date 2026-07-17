import { Fragment, useEffect, useMemo, useState } from 'react';
import { ShieldCheck, Save } from 'lucide-react';
import { type AdminRole } from '../api';
import { useRoles, usePermissionCatalog, useSetRolePermissions } from '../queries';
import { PageHeader, Panel, Button, EmptyState } from '../ui';
import { useToast } from '../toast';
import { getErrorMessage } from '../apiError';

const ADMIN_ROLE = 'Admin';

// Rótulos legíveis para recurso:ação (fallback = a própria chave técnica).
const RESOURCE_LABEL: Record<string, string> = {
  core: 'Cadastros base',
  registry: 'Cadastros de terceiros',
  payables: 'Contas a pagar',
  receivables: 'Contas a receber',
  treasury: 'Tesouraria',
  accounting: 'Contábil',
  fiscal: 'Fiscal',
  reports: 'Relatórios',
  admin: 'Administração',
  audit: 'Auditoria',
};
const ACTION_LABEL: Record<string, string> = {
  read: 'Ver',
  write: 'Escrita',
  approve: 'Aprovar',
  pay: 'Pagar',
  receive: 'Receber',
  post: 'Postar',
  users: 'Usuários',
};

// Rótulos legíveis para os cargos (o backend guarda o nome técnico em inglês; fallback = a própria chave).
const ROLE_LABEL: Record<string, string> = {
  Admin: 'Admin',
  User: 'Usuário',
  Finance: 'Financeiro',
  Controllership: 'Controladoria',
  Accounting: 'Contábil',
};

export function RolesPage() {
  const toast = useToast();
  const rolesQ = useRoles();
  const catalogQ = usePermissionCatalog();
  const setPerms = useSetRolePermissions();

  const roles = rolesQ.data ?? [];

  // Rascunho editável: roleId -> conjunto de permissões marcadas.
  const [draft, setDraft] = useState<Record<string, Set<string>>>({});
  useEffect(() => {
    if (!rolesQ.data) return;
    // Reinicializa apenas na primeira carga; edições não salvas são preservadas entre refetches.
    setDraft((prev) => {
      if (Object.keys(prev).length > 0) return prev;
      const next: Record<string, Set<string>> = {};
      for (const r of rolesQ.data) next[r.id] = new Set(r.permissions);
      return next;
    });
  }, [rolesQ.data]);

  const editableRoles = roles.filter((r) => r.name !== ADMIN_ROLE);
  const adminRole = roles.find((r) => r.name === ADMIN_ROLE);

  // Agrupa o catálogo por recurso (prefixo antes de ':').
  const groups = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const perm of catalogQ.data ?? []) {
      const resource = perm.split(':')[0];
      (map.get(resource) ?? map.set(resource, []).get(resource)!).push(perm);
    }
    return [...map.entries()];
  }, [catalogQ.data]);

  const toggle = (roleId: string, perm: string) =>
    setDraft((prev) => {
      const next = new Set(prev[roleId] ?? []);
      if (next.has(perm)) next.delete(perm);
      else next.add(perm);
      return { ...prev, [roleId]: next };
    });

  const isDirty = (role: AdminRole) => {
    const d = draft[role.id];
    if (!d) return false;
    return d.size !== role.permissions.length || role.permissions.some((p) => !d.has(p));
  };

  const save = (role: AdminRole) =>
    setPerms.mutate(
      { id: role.id, permissions: [...(draft[role.id] ?? [])] },
      {
        onSuccess: () => toast.success(`Permissões de ${ROLE_LABEL[role.name] ?? role.name} salvas.`),
        onError: (e) => toast.error(getErrorMessage(e)),
      },
    );

  if (rolesQ.isLoading || catalogQ.isLoading) {
    return <div className='empty-state'>Carregando…</div>;
  }
  if (editableRoles.length === 0) {
    return <EmptyState icon={<ShieldCheck />} title='Nenhum cargo' sub='Não há cargos configuráveis.' />;
  }

  return (
    <>
      <PageHeader
        title='Cargos & permissões'
        sub='Defina o que cada cargo pode fazer. As alterações valem no próximo login do usuário.'
      />

      <Panel title='Matriz de acesso'>
        <div style={{ overflowX: 'auto' }}>
          <table className='roles-matrix'>
            <thead>
              <tr>
                <th style={{ textAlign: 'left' }}>Permissão</th>
                {editableRoles.map((role) => (
                  <th key={role.id} style={{ textAlign: 'center', whiteSpace: 'nowrap' }}>
                    <div>{ROLE_LABEL[role.name] ?? role.name}</div>
                    <Button
                      icon={<Save size={14} />}
                      disabled={!isDirty(role) || setPerms.isPending}
                      onClick={() => save(role)}
                      style={{ marginTop: 4 }}
                    >
                      Salvar
                    </Button>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {groups.map(([resource, perms]) => (
                <Fragment key={resource}>
                  <tr>
                    <td colSpan={1 + editableRoles.length} style={{ paddingTop: 14 }}>
                      <strong>{RESOURCE_LABEL[resource] ?? resource}</strong>
                    </td>
                  </tr>
                  {perms.map((perm) => {
                    const action = perm.split(':')[1] ?? perm;
                    return (
                      <tr key={perm}>
                        <td>
                          {ACTION_LABEL[action] ?? action}{' '}
                          <span className='help t-mono' style={{ marginLeft: 6 }}>
                            {perm}
                          </span>
                        </td>
                        {editableRoles.map((role) => (
                          <td key={role.id} style={{ textAlign: 'center' }}>
                            <input
                              type='checkbox'
                              aria-label={`${ROLE_LABEL[role.name] ?? role.name} — ${perm}`}
                              checked={draft[role.id]?.has(perm) ?? false}
                              onChange={() => toggle(role.id, perm)}
                            />
                          </td>
                        ))}
                      </tr>
                    );
                  })}
                </Fragment>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>

      {adminRole && (
        <p className='help' style={{ marginTop: 12, display: 'flex', alignItems: 'center', gap: 6 }}>
          <ShieldCheck size={14} /> O cargo <strong>Admin</strong> tem acesso total (bypass) — não depende de permissões
          marcadas.
        </p>
      )}
    </>
  );
}
