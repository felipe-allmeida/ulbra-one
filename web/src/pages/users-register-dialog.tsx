import { useMemo, useState } from 'react';
import type { AdminRole, Company, CostCenter, Unit } from '../api';
import { parseMoneyInput } from '../lib';
import { APPROVER_ROLE, CONTROLLERSHIP_ROLE, isScopedRole, roleLabel } from '../permissions';
import { useCreateUser } from '../queries';
import { useToast } from '../toast';
import { getErrorMessage } from '../apiError';
import { Button, CurrencyInput, Field, Input, Modal, Select } from '../ui';
import { ScopeGroup, pairKey, parseCcKey, parseUnitKey } from './users-scope';

/**
 * Pre-registration dialog (convite). Sign-in is invitation-only, so a person only exists in the system once
 * an admin registers their e-mail here — with the role, and for the scoped roles the whole
 * Empresa × Unidade × Centro de custo scope, already set. The first Google login binds to this row and
 * carries exactly this access; until then the user is listed as "Aguardando 1º acesso".
 */
export function RegisterUserDialog({
  roles,
  companies,
  units,
  costCenters,
  onClose,
}: {
  roles: AdminRole[];
  companies: Company[];
  units: Unit[];
  costCenters: CostCenter[];
  onClose: () => void;
}) {
  const toast = useToast();
  const createUser = useCreateUser();

  const [email, setEmail] = useState('');
  const [name, setName] = useState('');
  const [role, setRole] = useState(roles[0]?.name ?? '');
  const [approvalLimit, setApprovalLimit] = useState('');
  const [canApprove, setCanApprove] = useState(false);
  const [companyIds, setCompanyIds] = useState<Set<string>>(new Set());
  const [unitKeys, setUnitKeys] = useState<Set<string>>(new Set());
  const [ccKeys, setCcKeys] = useState<Set<string>>(new Set());

  const scoped = isScopedRole(role);
  const grantedCompanies = useMemo(() => companies.filter((c) => companyIds.has(c.id)), [companies, companyIds]);

  const toggle = (set: Set<string>, key: string) => {
    const next = new Set(set);
    if (!next.delete(key)) next.add(key);
    return next;
  };

  /** Dropping a company also drops everything scoped under it — the API rejects orphans with 400. */
  const toggleCompany = (companyId: string) => {
    const next = toggle(companyIds, companyId);
    setCompanyIds(next);
    setUnitKeys(new Set([...unitKeys].filter((k) => next.has(parseUnitKey(k).companyId))));
    setCcKeys(new Set([...ccKeys].filter((k) => next.has(parseCcKey(k).companyId))));
  };

  const submit = async () => {
    if (!email.trim() || !role) return;
    try {
      await createUser.mutateAsync({
        email: email.trim(),
        name: name.trim(),
        role,
        companyIds: scoped ? [...companyIds] : [],
        units: scoped ? [...unitKeys].map(parseUnitKey) : [],
        costCenters: scoped ? [...ccKeys].map(parseCcKey) : [],
        approvalLimit: parseMoneyInput(approvalLimit),
        canApproveBudgetAdjustment: role === CONTROLLERSHIP_ROLE && canApprove,
      });
      toast.success(`${email.trim()} cadastrado. O acesso vale a partir do primeiro login com o Google.`);
      onClose();
    } catch (e) {
      toast.error(getErrorMessage(e));
    }
  };

  return (
    <Modal
      title='Cadastrar usuário'
      sub='O acesso é por convite: só entra quem foi cadastrado aqui. O cargo e o escopo já valem no primeiro login com o Google.'
      size={scoped ? 'wide' : 'default'}
      onClose={onClose}
      footer={
        <>
          <Button variant='ghost' onClick={onClose}>
            Cancelar
          </Button>
          <Button variant='brand' disabled={createUser.isPending} onClick={() => void submit()}>
            Cadastrar
          </Button>
        </>
      }
    >
      <div style={{ display: 'grid', gap: 16 }}>
        <Field label='E-mail' required help='O mesmo e-mail da conta Google que a pessoa usa para entrar.'>
          <Input
            aria-label='E-mail'
            type='email'
            value={email}
            placeholder='fulano@aelbra.com.br'
            onChange={(e) => setEmail(e.target.value)}
          />
        </Field>

        <Field label='Nome' help='Opcional — no primeiro login o nome da conta Google substitui este.'>
          <Input aria-label='Nome' value={name} onChange={(e) => setName(e.target.value)} />
        </Field>

        <Field label='Cargo' required>
          <Select aria-label='Cargo' value={role} onChange={(e) => setRole(e.target.value)}>
            {roles.map((r) => (
              <option key={r.id} value={r.name}>
                {roleLabel(r.name)}
              </option>
            ))}
          </Select>
        </Field>

        {(role === APPROVER_ROLE || role === CONTROLLERSHIP_ROLE) && (
          <Field label='Alçada de aprovação' help='Vazio = sem limite. Vale para adequações de orçamento.'>
            <CurrencyInput aria-label='Alçada de aprovação' value={approvalLimit} onValueChange={setApprovalLimit} />
          </Field>
        )}

        {role === CONTROLLERSHIP_ROLE && (
          <label style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
            <input
              type='checkbox'
              aria-label='Aprova Adequação'
              checked={canApprove}
              onChange={() => setCanApprove((v) => !v)}
            />
            Pode aprovar Adequação
          </label>
        )}

        {scoped && (
          <div style={{ display: 'grid', gap: 20 }}>
            <p className='help' style={{ margin: 0 }}>
              Sem regra de vazio: lista vazia = nenhum acesso. Você também pode deixar o escopo em branco agora e
              ajustá-lo na tabela depois.
            </p>

            <ScopeGroup
              title='Empresas'
              onSelectAll={() => setCompanyIds(new Set(companies.map((c) => c.id)))}
              selectAllLabel='Selecionar todas as empresas para o novo usuário'
            >
              <div style={{ display: 'grid', gap: 6 }}>
                {companies.map((c) => (
                  <label key={c.id} style={{ display: 'flex', alignItems: 'center', gap: 8, fontWeight: 500 }}>
                    <input
                      type='checkbox'
                      aria-label={`Empresa ${c.legalName} para o novo usuário`}
                      checked={companyIds.has(c.id)}
                      onChange={() => toggleCompany(c.id)}
                    />
                    {c.legalName}
                  </label>
                ))}
              </div>
            </ScopeGroup>

            <ScopeGroup
              title='Unidades'
              onSelectAll={() =>
                setUnitKeys(
                  new Set(units.filter((u) => companyIds.has(u.companyId)).map((u) => pairKey(u.companyId, u.id))),
                )
              }
              selectAllLabel='Selecionar todas as unidades para o novo usuário'
            >
              {grantedCompanies.length === 0 ? (
                <span className='help'>Marque ao menos uma empresa para liberar unidades.</span>
              ) : (
                <div style={{ display: 'grid', gap: 6 }}>
                  {units
                    .filter((u) => companyIds.has(u.companyId))
                    .map((u) => (
                      <label key={u.id} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <input
                          type='checkbox'
                          aria-label={`Unidade ${u.name} para o novo usuário`}
                          checked={unitKeys.has(pairKey(u.companyId, u.id))}
                          onChange={() => setUnitKeys(toggle(unitKeys, pairKey(u.companyId, u.id)))}
                        />
                        {u.name}
                      </label>
                    ))}
                </div>
              )}
            </ScopeGroup>

            <ScopeGroup
              title='Naturezas orçamentárias'
              onSelectAll={() =>
                setCcKeys(
                  new Set(
                    costCenters.filter((cc) => companyIds.has(cc.companyId)).map((cc) => pairKey(cc.companyId, cc.id)),
                  ),
                )
              }
              selectAllLabel='Selecionar todas as naturezas orçamentárias para o novo usuário'
            >
              {grantedCompanies.length === 0 ? (
                <span className='help'>Marque ao menos uma empresa para liberar naturezas orçamentárias.</span>
              ) : (
                <div style={{ display: 'grid', gap: 6 }}>
                  {costCenters
                    .filter((cc) => companyIds.has(cc.companyId))
                    .map((cc) => (
                      <label key={cc.id} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                        <input
                          type='checkbox'
                          aria-label={`Natureza orçamentária ${cc.name} para o novo usuário`}
                          checked={ccKeys.has(pairKey(cc.companyId, cc.id))}
                          onChange={() => setCcKeys(toggle(ccKeys, pairKey(cc.companyId, cc.id)))}
                        />
                        {cc.code} · {cc.name}
                      </label>
                    ))}
                </div>
              )}
            </ScopeGroup>
          </div>
        )}
      </div>
    </Modal>
  );
}
