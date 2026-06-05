import { useState } from 'react';
import { Sparkles } from 'lucide-react';
import {
  api,
  PersonType,
  type Address,
  type Vendor,
  type Customer,
  type Employee,
  type Unit,
  type CostCenter,
} from '../api';
import { Button, Field, Input, MaskedInput, Select } from '../ui';
import { useToast } from '../toast';
import { onlyDigits, maskCpf, maskCnpj, maskCep, maskPhone, isValidCpf, isValidCnpj } from '../format';

/* ---------- shared local state for an editable address ---------- */
const emptyAddress: Address = {
  cep: '',
  street: '',
  number: '',
  complement: '',
  neighborhood: '',
  city: '',
  state: '',
};

function fromAddress(a: Address | null): Address {
  return a ? { ...a, complement: a.complement ?? '' } : { ...emptyAddress };
}
/** Returns null if the address is entirely blank (so we don't persist empty objects). */
function toAddress(a: Address): Address | null {
  const filled = a.cep || a.street || a.number || a.neighborhood || a.city || a.state;
  if (!filled) return null;
  return { ...a, complement: a.complement?.trim() ? a.complement.trim() : null };
}

/** Fills only the address fields that are currently blank from a lookup result — never clobbers
 *  data the user already typed (important in edit mode). Returns the merged address + filled keys. */
function mergeAddressEmpty(
  current: Address,
  incoming: Partial<Address>,
): { address: Address; filled: (keyof Address)[] } {
  const out = { ...current };
  const filled: (keyof Address)[] = [];
  (['cep', 'street', 'number', 'neighborhood', 'city', 'state'] as (keyof Address)[]).forEach((k) => {
    const cur = (current[k] ?? '').toString();
    const inc = (incoming[k] ?? '').toString();
    if (!cur.trim() && inc) {
      out[k] = inc;
      filled.push(k);
    }
  });
  return { address: out, filled };
}

const AutoHint = () => (
  <span className='auto-hint'>
    <Sparkles /> auto
  </span>
);

/** Address sub-form with CEP auto-fill. `auto` marks which fields were just auto-filled. */
function AddressFields({ address, setAddress }: { address: Address; setAddress: (a: Address) => void }) {
  const toast = useToast();
  const [auto, setAuto] = useState<Set<keyof Address>>(new Set());
  const set = (patch: Partial<Address>) => setAddress({ ...address, ...patch });

  const onCepBlur = async () => {
    const cep = onlyDigits(address.cep);
    if (cep.length !== 8) return;
    try {
      const r = await api.lookupCep(cep);
      const { address: merged, filled } = mergeAddressEmpty(address, {
        street: r.street,
        neighborhood: r.neighborhood,
        city: r.city,
        state: r.state,
      });
      if (filled.length === 0) return; // nothing blank to fill — don't overwrite the user's data
      setAddress(merged);
      setAuto(new Set(filled));
      toast.success('Endereço preenchido pelo CEP.');
    } catch {
      toast.error('CEP não encontrado. Preencha o endereço manualmente.');
    }
  };

  const cls = (k: keyof Address) => (auto.has(k) ? 'input-auto' : undefined);
  const clearAuto = (k: keyof Address) => {
    if (auto.has(k)) {
      const n = new Set(auto);
      n.delete(k);
      setAuto(n);
    }
  };

  return (
    <>
      <div className='form-section'>Endereço</div>
      <div className='form-grid'>
        <Field label='CEP'>
          <MaskedInput
            value={address.cep}
            mask={maskCep}
            onValueChange={(v) => set({ cep: v })}
            onBlur={onCepBlur}
            placeholder='00000-000'
            inputMode='numeric'
          />
        </Field>
        <Field label='Número'>
          <Input value={address.number} onChange={(e) => set({ number: e.target.value })} />
        </Field>
        <div className='span-2'>
          <Field label={<>Logradouro {auto.has('street') && <AutoHint />}</>}>
            <Input
              className={cls('street')}
              value={address.street}
              onChange={(e) => {
                clearAuto('street');
                set({ street: e.target.value });
              }}
            />
          </Field>
        </div>
        <Field label='Complemento'>
          <Input value={address.complement ?? ''} onChange={(e) => set({ complement: e.target.value })} />
        </Field>
        <Field label={<>Bairro {auto.has('neighborhood') && <AutoHint />}</>}>
          <Input
            className={cls('neighborhood')}
            value={address.neighborhood}
            onChange={(e) => {
              clearAuto('neighborhood');
              set({ neighborhood: e.target.value });
            }}
          />
        </Field>
        <Field label={<>Cidade {auto.has('city') && <AutoHint />}</>}>
          <Input
            className={cls('city')}
            value={address.city}
            onChange={(e) => {
              clearAuto('city');
              set({ city: e.target.value });
            }}
          />
        </Field>
        <Field label={<>UF {auto.has('state') && <AutoHint />}</>}>
          <Input
            className={cls('state')}
            maxLength={2}
            value={address.state}
            onChange={(e) => {
              clearAuto('state');
              set({ state: e.target.value.toUpperCase() });
            }}
          />
        </Field>
      </div>
    </>
  );
}

/* ============================ VENDOR ============================ */
export function VendorForm({
  companyId,
  initial,
  onSaved,
  onCancel,
}: {
  companyId: string;
  initial?: Vendor;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const toast = useToast();
  const [personType, setPersonType] = useState<PersonType>(initial?.personType ?? PersonType.Company);
  const [name, setName] = useState(initial?.name ?? '');
  const [tradeName, setTradeName] = useState(initial?.tradeName ?? '');
  const [document, setDocument] = useState(initial?.document ?? '');
  const [email, setEmail] = useState(initial?.email ?? '');
  const [phone, setPhone] = useState(initial?.phone ?? '');
  const [paymentTerms, setPaymentTerms] = useState(initial?.paymentTerms ?? '');
  const [bankInfo, setBankInfo] = useState(initial?.bankInfo ?? '');
  const [address, setAddress] = useState<Address>(fromAddress(initial?.address ?? null));
  const [nameAuto, setNameAuto] = useState<Set<'name' | 'tradeName'>>(new Set());
  const [saving, setSaving] = useState(false);

  const isPj = personType === PersonType.Company;
  const docDigits = onlyDigits(document);
  const docValid = docDigits.length === 0 || (isPj ? isValidCnpj(docDigits) : isValidCpf(docDigits));
  const canSave = !!name.trim() && docDigits.length > 0 && docValid && !saving;

  const onCnpjBlur = async () => {
    if (!isPj || onlyDigits(document).length !== 14) return;
    try {
      const r = await api.lookupCnpj(onlyDigits(document));
      const autoFilled = new Set<'name' | 'tradeName'>();
      if (!name.trim()) {
        setName(r.razaoSocial);
        autoFilled.add('name');
      }
      if (!tradeName.trim() && r.nomeFantasia) {
        setTradeName(r.nomeFantasia);
        autoFilled.add('tradeName');
      }
      if (r.address) setAddress((prev) => mergeAddressEmpty(prev, r.address!).address);
      if (autoFilled.size) setNameAuto(autoFilled);
      toast.success('Dados preenchidos pelo CNPJ.');
    } catch {
      toast.error('CNPJ não encontrado. Preencha os dados manualmente.');
    }
  };

  const save = async () => {
    setSaving(true);
    try {
      const body = {
        companyId,
        personType,
        name: name.trim(),
        tradeName: tradeName.trim() || null,
        document: docDigits,
        email: email.trim() || null,
        phone: phone.trim() || null,
        address: toAddress(address),
        paymentTerms: paymentTerms.trim() || null,
        bankInfo: bankInfo.trim() || null,
      };
      if (initial) await api.updateVendor(initial.id, body);
      else await api.createVendor(body);
      toast.success(initial ? 'Fornecedor atualizado.' : 'Fornecedor criado.');
      onSaved();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Falha ao salvar.');
      setSaving(false);
    }
  };

  return (
    <>
      <PersonTypeToggle value={personType} onChange={setPersonType} />
      <div className='form-grid'>
        <div className='span-2'>
          <Field
            label={isPj ? 'CNPJ' : 'CPF'}
            help={!docValid ? undefined : 'Dados podem ser preenchidos automaticamente.'}
          >
            <MaskedInput
              value={document}
              mask={isPj ? maskCnpj : maskCpf}
              onValueChange={setDocument}
              onBlur={onCnpjBlur}
              className={!docValid ? 'input-err' : undefined}
              placeholder={isPj ? '00.000.000/0000-00' : '000.000.000-00'}
              inputMode='numeric'
            />
            {!docValid && <div className='field-err'>{isPj ? 'CNPJ inválido.' : 'CPF inválido.'}</div>}
          </Field>
        </div>
        <div className='span-2'>
          <Field
            label={
              <>
                {isPj ? 'Razão social' : 'Nome'} {nameAuto.has('name') && <AutoHint />}
              </>
            }
          >
            <Input
              className={nameAuto.has('name') ? 'input-auto' : undefined}
              value={name}
              onChange={(e) => setName(e.target.value)}
              autoFocus
            />
          </Field>
        </div>
        {isPj && (
          <div className='span-2'>
            <Field label={<>Nome fantasia {nameAuto.has('tradeName') && <AutoHint />}</>}>
              <Input
                className={nameAuto.has('tradeName') ? 'input-auto' : undefined}
                value={tradeName}
                onChange={(e) => setTradeName(e.target.value)}
              />
            </Field>
          </div>
        )}
        <Field label='E-mail'>
          <Input type='email' value={email} onChange={(e) => setEmail(e.target.value)} />
        </Field>
        <Field label='Telefone'>
          <MaskedInput
            value={phone}
            mask={maskPhone}
            onValueChange={setPhone}
            placeholder='(00) 00000-0000'
            inputMode='tel'
          />
        </Field>
      </div>

      <AddressFields address={address} setAddress={setAddress} />

      <div className='form-section'>Comercial</div>
      <div className='form-grid'>
        <Field label='Condição de pagamento'>
          <Input value={paymentTerms} onChange={(e) => setPaymentTerms(e.target.value)} placeholder='ex.: 30 dias' />
        </Field>
        <Field label='Dados bancários'>
          <Input value={bankInfo} onChange={(e) => setBankInfo(e.target.value)} placeholder='Banco / ag. / conta' />
        </Field>
      </div>

      <FormActions canSave={canSave} saving={saving} onSave={save} onCancel={onCancel} editing={!!initial} />
    </>
  );
}

/* ============================ CUSTOMER ============================ */
export function CustomerForm({
  companyId,
  initial,
  onSaved,
  onCancel,
}: {
  companyId: string;
  initial?: Customer;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const toast = useToast();
  const [personType, setPersonType] = useState<PersonType>(initial?.personType ?? PersonType.Company);
  const [name, setName] = useState(initial?.name ?? '');
  const [tradeName, setTradeName] = useState(initial?.tradeName ?? '');
  const [document, setDocument] = useState(initial?.document ?? '');
  const [email, setEmail] = useState(initial?.email ?? '');
  const [phone, setPhone] = useState(initial?.phone ?? '');
  const [creditLimit, setCreditLimit] = useState(initial?.creditLimit != null ? String(initial.creditLimit) : '');
  const [address, setAddress] = useState<Address>(fromAddress(initial?.address ?? null));
  const [nameAuto, setNameAuto] = useState<Set<'name' | 'tradeName'>>(new Set());
  const [saving, setSaving] = useState(false);

  const isPj = personType === PersonType.Company;
  const docDigits = onlyDigits(document);
  const docValid = docDigits.length === 0 || (isPj ? isValidCnpj(docDigits) : isValidCpf(docDigits));
  const canSave = !!name.trim() && docDigits.length > 0 && docValid && !saving;

  const onCnpjBlur = async () => {
    if (!isPj || onlyDigits(document).length !== 14) return;
    try {
      const r = await api.lookupCnpj(onlyDigits(document));
      const autoFilled = new Set<'name' | 'tradeName'>();
      if (!name.trim()) {
        setName(r.razaoSocial);
        autoFilled.add('name');
      }
      if (!tradeName.trim() && r.nomeFantasia) {
        setTradeName(r.nomeFantasia);
        autoFilled.add('tradeName');
      }
      if (r.address) setAddress((prev) => mergeAddressEmpty(prev, r.address!).address);
      if (autoFilled.size) setNameAuto(autoFilled);
      toast.success('Dados preenchidos pelo CNPJ.');
    } catch {
      toast.error('CNPJ não encontrado. Preencha os dados manualmente.');
    }
  };

  const save = async () => {
    setSaving(true);
    try {
      const body = {
        companyId,
        personType,
        name: name.trim(),
        tradeName: tradeName.trim() || null,
        document: docDigits,
        email: email.trim() || null,
        phone: phone.trim() || null,
        address: toAddress(address),
        creditLimit: creditLimit.trim() ? Number(creditLimit) : null,
      };
      if (initial) await api.updateCustomer(initial.id, body);
      else await api.createCustomer(body);
      toast.success(initial ? 'Cliente atualizado.' : 'Cliente criado.');
      onSaved();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Falha ao salvar.');
      setSaving(false);
    }
  };

  return (
    <>
      <PersonTypeToggle value={personType} onChange={setPersonType} />
      <div className='form-grid'>
        <div className='span-2'>
          <Field label={isPj ? 'CNPJ' : 'CPF'}>
            <MaskedInput
              value={document}
              mask={isPj ? maskCnpj : maskCpf}
              onValueChange={setDocument}
              onBlur={onCnpjBlur}
              className={!docValid ? 'input-err' : undefined}
              placeholder={isPj ? '00.000.000/0000-00' : '000.000.000-00'}
              inputMode='numeric'
            />
            {!docValid && <div className='field-err'>{isPj ? 'CNPJ inválido.' : 'CPF inválido.'}</div>}
          </Field>
        </div>
        <div className='span-2'>
          <Field
            label={
              <>
                {isPj ? 'Razão social' : 'Nome'} {nameAuto.has('name') && <AutoHint />}
              </>
            }
          >
            <Input
              className={nameAuto.has('name') ? 'input-auto' : undefined}
              value={name}
              onChange={(e) => setName(e.target.value)}
              autoFocus
            />
          </Field>
        </div>
        {isPj && (
          <div className='span-2'>
            <Field label={<>Nome fantasia {nameAuto.has('tradeName') && <AutoHint />}</>}>
              <Input
                className={nameAuto.has('tradeName') ? 'input-auto' : undefined}
                value={tradeName}
                onChange={(e) => setTradeName(e.target.value)}
              />
            </Field>
          </div>
        )}
        <Field label='E-mail'>
          <Input type='email' value={email} onChange={(e) => setEmail(e.target.value)} />
        </Field>
        <Field label='Telefone'>
          <MaskedInput
            value={phone}
            mask={maskPhone}
            onValueChange={setPhone}
            placeholder='(00) 00000-0000'
            inputMode='tel'
          />
        </Field>
        <Field label='Limite de crédito (R$)'>
          <Input
            value={creditLimit}
            inputMode='numeric'
            onChange={(e) => setCreditLimit(e.target.value)}
            placeholder='ex.: 10000'
          />
        </Field>
      </div>

      <AddressFields address={address} setAddress={setAddress} />

      <FormActions canSave={canSave} saving={saving} onSave={save} onCancel={onCancel} editing={!!initial} />
    </>
  );
}

/* ============================ EMPLOYEE ============================ */
export function EmployeeForm({
  companyId,
  units,
  costCenters,
  initial,
  onSaved,
  onCancel,
}: {
  companyId: string;
  units: Unit[];
  costCenters: CostCenter[];
  initial?: Employee;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const toast = useToast();
  const [name, setName] = useState(initial?.name ?? '');
  const [document, setDocument] = useState(initial?.document ?? '');
  const [email, setEmail] = useState(initial?.email ?? '');
  const [phone, setPhone] = useState(initial?.phone ?? '');
  const [role, setRole] = useState(initial?.role ?? '');
  const [unitId, setUnitId] = useState(initial?.unitId ?? '');
  const [costCenterId, setCostCenterId] = useState(initial?.costCenterId ?? '');
  const [hireDate, setHireDate] = useState(initial?.hireDate ?? '');
  const [address, setAddress] = useState<Address>(fromAddress(initial?.address ?? null));
  const [saving, setSaving] = useState(false);

  const docDigits = onlyDigits(document);
  const docValid = docDigits.length === 0 || isValidCpf(docDigits);
  const canSave =
    !!name.trim() && !!role.trim() && !!unitId && !!hireDate && docDigits.length > 0 && docValid && !saving;

  const save = async () => {
    setSaving(true);
    try {
      const body = {
        companyId,
        name: name.trim(),
        document: docDigits,
        email: email.trim() || null,
        phone: phone.trim() || null,
        role: role.trim(),
        unitId,
        costCenterId: costCenterId || null,
        hireDate,
        address: toAddress(address),
      };
      if (initial) await api.updateEmployee(initial.id, body);
      else await api.createEmployee(body);
      toast.success(initial ? 'Colaborador atualizado.' : 'Colaborador criado.');
      onSaved();
    } catch (e) {
      toast.error(e instanceof Error ? e.message : 'Falha ao salvar.');
      setSaving(false);
    }
  };

  return (
    <>
      <div className='form-grid'>
        <div className='span-2'>
          <Field label='Nome'>
            <Input value={name} onChange={(e) => setName(e.target.value)} autoFocus />
          </Field>
        </div>
        <Field label='CPF'>
          <MaskedInput
            value={document}
            mask={maskCpf}
            onValueChange={setDocument}
            className={!docValid ? 'input-err' : undefined}
            placeholder='000.000.000-00'
            inputMode='numeric'
          />
          {!docValid && <div className='field-err'>CPF inválido.</div>}
        </Field>
        <Field label='Cargo'>
          <Input value={role} onChange={(e) => setRole(e.target.value)} placeholder='ex.: Analista' />
        </Field>
        <Field label='E-mail'>
          <Input type='email' value={email} onChange={(e) => setEmail(e.target.value)} />
        </Field>
        <Field label='Telefone'>
          <MaskedInput
            value={phone}
            mask={maskPhone}
            onValueChange={setPhone}
            placeholder='(00) 00000-0000'
            inputMode='tel'
          />
        </Field>
        <Field label='Unidade'>
          <Select value={unitId} onChange={(e) => setUnitId(e.target.value)}>
            <option value=''>— selecione —</option>
            {units.map((u) => (
              <option key={u.id} value={u.id}>
                {u.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label='Centro de custo'>
          <Select value={costCenterId} onChange={(e) => setCostCenterId(e.target.value)}>
            <option value=''>— opcional —</option>
            {costCenters.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </Select>
        </Field>
        <Field label='Data de admissão'>
          <Input type='date' value={hireDate} onChange={(e) => setHireDate(e.target.value)} />
        </Field>
      </div>

      <AddressFields address={address} setAddress={setAddress} />

      <FormActions canSave={canSave} saving={saving} onSave={save} onCancel={onCancel} editing={!!initial} />
    </>
  );
}

/* ---------- small shared bits ---------- */
function PersonTypeToggle({ value, onChange }: { value: PersonType; onChange: (v: PersonType) => void }) {
  return (
    <div className='seg' role='tablist'>
      <button
        type='button'
        className={value === PersonType.Company ? 'on' : ''}
        onClick={() => onChange(PersonType.Company)}
      >
        Pessoa jurídica
      </button>
      <button
        type='button'
        className={value === PersonType.Individual ? 'on' : ''}
        onClick={() => onChange(PersonType.Individual)}
      >
        Pessoa física
      </button>
    </div>
  );
}

export function FormActions({
  canSave,
  saving,
  onSave,
  onCancel,
  editing,
}: {
  canSave: boolean;
  saving: boolean;
  onSave: () => void;
  onCancel: () => void;
  editing: boolean;
}) {
  return (
    <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end', marginTop: 6 }}>
      <Button onClick={onCancel} disabled={saving}>
        Cancelar
      </Button>
      <Button variant='brand' disabled={!canSave} onClick={onSave}>
        {saving ? 'Salvando…' : editing ? 'Salvar' : 'Criar'}
      </Button>
    </div>
  );
}
