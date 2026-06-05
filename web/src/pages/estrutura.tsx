import { useEffect, useState } from 'react';
import { Plus, Pencil } from 'lucide-react';
import { api, type Company, type Unit } from '../api';
import { useApp } from '../store';
import { Button, Input, PageHeader, Panel, useSave } from '../ui';
import { EmpresaPicker } from './shared';

export function EstruturaPage() {
  const { empresas, empresa, empresaId, refreshEmpresas } = useApp();
  const [unidades, setUnidades] = useState<Unit[]>([]);
  const [empresaForm, setEmpresaForm] = useState<null | { edit?: Company }>(null);
  const [unidadeForm, setUnidadeForm] = useState<null | { edit?: Unit }>(null);

  const refreshUnidades = () => {
    if (empresaId)
      api
        .listUnits(empresaId)
        .then(setUnidades)
        .catch(() => {});
  };
  useEffect(() => {
    if (!empresaId) {
      setUnidades([]);
      return;
    }
    refreshUnidades();
  }, [empresaId]);

  return (
    <>
      <PageHeader
        title='Empresas e unidades'
        sub='Pessoas jurídicas (CNPJ) do grupo e suas unidades (campus, polos).'
      />

      <div style={{ marginBottom: 16 }}>
        <Panel
          title='Empresas'
          action={
            <Button variant='brand' icon={<Plus />} onClick={() => setEmpresaForm({})}>
              Nova empresa
            </Button>
          }
        >
          {empresaForm && (
            <InlineEmpresaForm
              initial={empresaForm.edit}
              onClose={() => setEmpresaForm(null)}
              onSaved={async (e) => {
                await refreshEmpresas(e.id);
                setEmpresaForm(null);
              }}
            />
          )}
          <table>
            <thead>
              <tr>
                <th>Razão social</th>
                <th>CNPJ</th>
                <th>Status</th>
                <th className='td-action'></th>
              </tr>
            </thead>
            <tbody>
              {empresas.length === 0 ? (
                <tr>
                  <td colSpan={4} className='t-empty'>
                    Nenhuma empresa cadastrada.
                  </td>
                </tr>
              ) : (
                empresas.map((e) => (
                  <tr key={e.id}>
                    <td>{e.legalName}</td>
                    <td className='t-mono'>{e.cnpj}</td>
                    <td>{e.active ? 'Ativa' : 'Inativa'}</td>
                    <td className='td-action'>
                      <button className='icon-btn' onClick={() => setEmpresaForm({ edit: e })} aria-label='Editar'>
                        <Pencil />
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </Panel>
      </div>

      <Panel
        title='Unidades'
        action={
          <div className='toolbar'>
            <EmpresaPicker />
            <Button variant='brand' icon={<Plus />} disabled={!empresa} onClick={() => setUnidadeForm({})}>
              Nova unidade
            </Button>
          </div>
        }
      >
        {unidadeForm && empresa && (
          <InlineUnidadeForm
            empresa={empresa}
            initial={unidadeForm.edit}
            onClose={() => setUnidadeForm(null)}
            onSaved={() => {
              refreshUnidades();
              setUnidadeForm(null);
            }}
          />
        )}
        <table>
          <thead>
            <tr>
              <th>Nome</th>
              <th>Sigla</th>
              <th>Status</th>
              <th className='td-action'></th>
            </tr>
          </thead>
          <tbody>
            {!empresa ? (
              <tr>
                <td colSpan={4} className='t-empty'>
                  Selecione uma empresa.
                </td>
              </tr>
            ) : unidades.length === 0 ? (
              <tr>
                <td colSpan={4} className='t-empty'>
                  Nenhuma unidade nesta empresa.
                </td>
              </tr>
            ) : (
              unidades.map((u) => (
                <tr key={u.id}>
                  <td>{u.name}</td>
                  <td className='t-mono'>{u.acronym}</td>
                  <td>{u.active ? 'Ativa' : 'Inativa'}</td>
                  <td className='td-action'>
                    <button className='icon-btn' onClick={() => setUnidadeForm({ edit: u })} aria-label='Editar'>
                      <Pencil />
                    </button>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </Panel>
    </>
  );
}

function InlineEmpresaForm({
  initial,
  onClose,
  onSaved,
}: {
  initial?: Company;
  onClose: () => void;
  onSaved: (e: Company) => void;
}) {
  const [razao, setRazao] = useState(initial?.legalName ?? '');
  const [cnpj, setCnpj] = useState(initial?.cnpj ?? '');
  const { salvando, erro, run } = useSave(async () => {
    const e = initial
      ? await api.updateCompany(initial.id, razao.trim(), cnpj.trim())
      : await api.createCompany(razao.trim(), cnpj.trim());
    onSaved(e);
  });
  return (
    <div className='inline-form'>
      <div className='grow'>
        <Input autoFocus placeholder='Razão social' value={razao} onChange={(e) => setRazao(e.target.value)} />
      </div>
      <Input placeholder='CNPJ (14 dígitos)' value={cnpj} onChange={(e) => setCnpj(e.target.value)} />
      <Button variant='brand' disabled={!razao.trim() || !cnpj.trim() || salvando} onClick={run}>
        {initial ? 'Salvar' : 'Adicionar'}
      </Button>
      <Button onClick={onClose}>Cancelar</Button>
      {erro && (
        <div className='create-err' style={{ flexBasis: '100%' }}>
          {erro}
        </div>
      )}
    </div>
  );
}

function InlineUnidadeForm({
  empresa,
  initial,
  onClose,
  onSaved,
}: {
  empresa: Company;
  initial?: Unit;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [nome, setNome] = useState(initial?.name ?? '');
  const [sigla, setSigla] = useState(initial?.acronym ?? '');
  const { salvando, erro, run } = useSave(async () => {
    initial
      ? await api.updateUnit(initial.id, nome.trim(), sigla.trim())
      : await api.createUnit(empresa.id, nome.trim(), sigla.trim());
    onSaved();
  });
  return (
    <div className='inline-form'>
      <div className='grow'>
        <Input autoFocus placeholder='Nome da unidade' value={nome} onChange={(e) => setNome(e.target.value)} />
      </div>
      <Input placeholder='Sigla' value={sigla} onChange={(e) => setSigla(e.target.value)} />
      <Button variant='brand' disabled={!nome.trim() || !sigla.trim() || salvando} onClick={run}>
        {initial ? 'Salvar' : 'Adicionar'}
      </Button>
      <Button onClick={onClose}>Cancelar</Button>
      {erro && (
        <div className='create-err' style={{ flexBasis: '100%' }}>
          {erro}
        </div>
      )}
    </div>
  );
}
