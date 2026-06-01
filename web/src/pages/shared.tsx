import { useState } from 'react';
import { api, type Empresa, type Unidade, type CentroDeCusto } from '../api';
import { useApp } from '../store';
import { Button, Input, Select, Field, ToolbarField, useSave } from '../ui';

export function EmpresaPicker() {
  const { empresas, empresaId, setEmpresaId } = useApp();
  return (
    <ToolbarField label="Empresa">
      <Select value={empresaId} onChange={(e) => setEmpresaId(e.target.value)}>
        {empresas.length === 0 && <option value="">— nenhuma —</option>}
        {empresas.map((e) => <option key={e.id} value={e.id}>{e.razaoSocial}</option>)}
      </Select>
    </ToolbarField>
  );
}

export function UnidadePicker({ unidades, sel, setSel }: { unidades: Unidade[]; sel: Set<string>; setSel: (s: Set<string>) => void }) {
  const todas = unidades.length > 0 && sel.size === unidades.length;
  const toggle = (id: string) => { const n = new Set(sel); n.has(id) ? n.delete(id) : n.add(id); setSel(n); };
  if (unidades.length === 0) return <div className="uni-pick-empty">Cadastre uma unidade primeiro (em Estrutura).</div>;
  return (
    <div className="uni-pick">
      <label className="uni-pick-row" style={{ fontWeight: 600 }}>
        <input type="checkbox" checked={todas} onChange={() => setSel(todas ? new Set() : new Set(unidades.map((u) => u.id)))} /> Todas as unidades
      </label>
      <div className="uni-pick-sep" />
      {unidades.map((u) => (
        <label className="uni-pick-row" key={u.id}>
          <input type="checkbox" checked={sel.has(u.id)} onChange={() => toggle(u.id)} /> {u.nome} <span className="sig">{u.sigla}</span>
        </label>
      ))}
    </div>
  );
}

/** Formulário de centro de custo (criar ou editar). */
export function CentroForm({ empresa, unidades, initial, onSaved, onCancel }: {
  empresa: Empresa; unidades: Unidade[]; initial?: CentroDeCusto;
  onSaved: (cc: CentroDeCusto) => void; onCancel: () => void;
}) {
  const [codigo, setCodigo] = useState(initial?.codigo ?? '');
  const [nome, setNome] = useState(initial?.nome ?? '');
  const [orc, setOrc] = useState(initial ? String(initial.orcamentoAnual) : '');
  const [limite, setLimite] = useState(initial ? String(initial.limiteAutoAprovacao) : '');
  const [sel, setSel] = useState<Set<string>>(new Set(initial?.unidadeIds ?? []));

  const { salvando, erro, run } = useSave(async () => {
    const o = Number(orc) || 0, l = Number(limite) || 0;
    const cc = initial
      ? await api.updateCentro(initial.id, codigo.trim(), nome.trim(), o, l, [...sel])
      : await api.createCentro(empresa.id, codigo.trim(), nome.trim(), o, l, [...sel]);
    onSaved(cc);
  });

  return (
    <div>
      <div className="edit-grid">
        <Field label="Código"><Input autoFocus value={codigo} onChange={(e) => setCodigo(e.target.value)} placeholder="ex.: MKT" /></Field>
        <Field label="Nome"><Input value={nome} onChange={(e) => setNome(e.target.value)} placeholder="ex.: Marketing" /></Field>
        <Field label="Orçamento anual (R$)"><Input value={orc} inputMode="numeric" onChange={(e) => setOrc(e.target.value)} placeholder="ex.: 500000" /></Field>
        <Field label="Limite de auto-aprovação (R$)" help="Despesas até este valor são aprovadas automaticamente. 0 = sem auto-aprovação.">
          <Input value={limite} inputMode="numeric" onChange={(e) => setLimite(e.target.value)} placeholder="ex.: 5000" />
        </Field>
      </div>
      <div style={{ marginTop: 14 }}>
        <Field label="Atua nas unidades"><UnidadePicker unidades={unidades} sel={sel} setSel={setSel} /></Field>
      </div>
      {erro && <div className="create-err" style={{ marginTop: 10 }}>{erro}</div>}
      <div style={{ display: 'flex', gap: 8, marginTop: 18 }}>
        <Button variant="brand" disabled={!codigo.trim() || !nome.trim() || salvando} onClick={run}>{salvando ? 'Salvando…' : 'Salvar'}</Button>
        <Button onClick={onCancel}>Cancelar</Button>
      </div>
    </div>
  );
}
