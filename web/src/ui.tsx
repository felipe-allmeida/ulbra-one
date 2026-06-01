import {
  useState, type ReactNode, type InputHTMLAttributes, type SelectHTMLAttributes, type ButtonHTMLAttributes,
} from 'react';
import { X } from 'lucide-react';

export const cx = (...c: (string | false | undefined | null)[]) => c.filter(Boolean).join(' ');

/* ---------- Button ---------- */
type BtnProps = { variant?: 'default' | 'brand' | 'ghost'; icon?: ReactNode } & ButtonHTMLAttributes<HTMLButtonElement>;
export function Button({ variant = 'default', icon, children, className, ...p }: BtnProps) {
  return (
    <button className={cx('btn', variant === 'brand' && 'btn-brand', variant === 'ghost' && 'btn-ghost', className)} {...p}>
      {icon}{children}
    </button>
  );
}

/* ---------- Inputs ---------- */
export function Input({ className, ...p }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cx('input', className)} {...p} />;
}
export function Select({ className, children, ...p }: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className={cx('select', className)} {...p}>{children}</select>;
}

/** Campo de formulário (modal): label em cima, ajuda embaixo. */
export function Field({ label, help, children }: { label: string; help?: string; children: ReactNode }) {
  return <div className="form-field"><label>{label}</label>{children}{help && <div className="help">{help}</div>}</div>;
}

/** Campo de toolbar (label maiúsculo compacto). */
export function ToolbarField({ label, children }: { label: string; children: ReactNode }) {
  return <div className="field"><label>{label}</label>{children}</div>;
}

/* ---------- Chip ---------- */
export function Chip({ tone = 'default', icon, children }: { tone?: 'default' | 'brand' | 'auto'; icon?: ReactNode; children: ReactNode }) {
  return <span className={cx('chip', tone === 'brand' && 'all', tone === 'auto' && 'auto')}>{icon}{children}</span>;
}

/* ---------- KPI ---------- */
export function Kpi({ label, value, foot }: { label: string; value: string; foot?: string }) {
  return <div className="kpi-card"><div className="kpi-label">{label}</div><div className="kpi-value">{value}</div>{foot && <div className="kpi-foot">{foot}</div>}</div>;
}

/* ---------- Empty state ---------- */
export function EmptyState({ icon, title, sub }: { icon: ReactNode; title: string; sub: string }) {
  return <div className="empty-state">{icon}<div className="t">{title}</div><div className="s">{sub}</div></div>;
}

/* ---------- Page header ---------- */
export function PageHeader({ title, sub, children }: { title: string; sub?: string; children?: ReactNode }) {
  return (
    <div className="page-head">
      <div><h1 className="page-title">{title}</h1>{sub && <p className="page-sub">{sub}</p>}</div>
      {children && <div className="toolbar">{children}</div>}
    </div>
  );
}

/* ---------- Panel + Table ---------- */
export function Panel({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <div className="panel">
      <div className="panel-head"><span className="panel-title">{title}</span>{action}</div>
      {children}
    </div>
  );
}

/* ---------- Modal ---------- */
export function Modal({ title, sub, onClose, footer, children }: {
  title: string; sub?: string; onClose: () => void; footer: ReactNode; children: ReactNode;
}) {
  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
            <div><div className="modal-title">{title}</div>{sub && <div className="modal-sub">{sub}</div>}</div>
            <button className="icon-btn" onClick={onClose} aria-label="Fechar"><X /></button>
          </div>
        </div>
        <div className="modal-body">{children}</div>
        <div className="modal-foot">{footer}</div>
      </div>
    </div>
  );
}

/** Hook de salvamento com estado de loading + erro. */
export function useSave(fn: () => Promise<void>) {
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const run = async () => {
    setErro(null); setSalvando(true);
    try { await fn(); } catch (e) { setErro(String(e)); setSalvando(false); }
  };
  return { salvando, erro, run };
}
