import {
  useState,
  useEffect,
  type ReactNode,
  type InputHTMLAttributes,
  type SelectHTMLAttributes,
  type ButtonHTMLAttributes,
} from 'react';
import { X, AlertTriangle } from 'lucide-react';

export const cx = (...c: (string | false | undefined | null)[]) => c.filter(Boolean).join(' ');

/* ---------- Button ---------- */
type BtnProps = { variant?: 'default' | 'brand' | 'ghost'; icon?: ReactNode } & ButtonHTMLAttributes<HTMLButtonElement>;
export function Button({ variant = 'default', icon, children, className, ...p }: BtnProps) {
  return (
    <button
      className={cx('btn', variant === 'brand' && 'btn-brand', variant === 'ghost' && 'btn-ghost', className)}
      {...p}
    >
      {icon}
      {children}
    </button>
  );
}

/* ---------- Inputs ---------- */
export function Input({ className, ...p }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cx('input', className)} {...p} />;
}
export function Select({ className, children, ...p }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <select className={cx('select', className)} {...p}>
      {children}
    </select>
  );
}

/** Campo de formulário (modal): label em cima, ajuda embaixo. */
export function Field({ label, help, children }: { label: ReactNode; help?: string; children: ReactNode }) {
  return (
    <div className='form-field'>
      <label>{label}</label>
      {children}
      {help && <div className='help'>{help}</div>}
    </div>
  );
}

/** Campo de toolbar (label maiúsculo compacto). */
export function ToolbarField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className='field'>
      <label>{label}</label>
      {children}
    </div>
  );
}

/* ---------- Chip ---------- */
export function Chip({
  tone = 'default',
  icon,
  children,
}: {
  tone?: 'default' | 'brand' | 'auto';
  icon?: ReactNode;
  children: ReactNode;
}) {
  return (
    <span className={cx('chip', tone === 'brand' && 'all', tone === 'auto' && 'auto')}>
      {icon}
      {children}
    </span>
  );
}

/* ---------- KPI ---------- */
export function Kpi({ label, value, foot }: { label: string; value: string; foot?: string }) {
  return (
    <div className='kpi-card'>
      <div className='kpi-label'>{label}</div>
      <div className='kpi-value'>{value}</div>
      {foot && <div className='kpi-foot'>{foot}</div>}
    </div>
  );
}

/* ---------- Empty state ---------- */
export function EmptyState({ icon, title, sub }: { icon: ReactNode; title: string; sub: string }) {
  return (
    <div className='empty-state'>
      {icon}
      <div className='t'>{title}</div>
      <div className='s'>{sub}</div>
    </div>
  );
}

/* ---------- Page header ---------- */
export function PageHeader({ title, sub, children }: { title: string; sub?: string; children?: ReactNode }) {
  return (
    <div className='page-head'>
      <div>
        <h1 className='page-title'>{title}</h1>
        {sub && <p className='page-sub'>{sub}</p>}
      </div>
      {children && <div className='toolbar'>{children}</div>}
    </div>
  );
}

/* ---------- Panel + Table ---------- */
export function Panel({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <div className='panel'>
      <div className='panel-head'>
        <span className='panel-title'>{title}</span>
        {action}
      </div>
      {children}
    </div>
  );
}

/* ---------- Modal ---------- */
export function Modal({
  title,
  sub,
  onClose,
  footer,
  children,
}: {
  title: string;
  sub?: string;
  onClose: () => void;
  footer: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className='modal-overlay' onClick={onClose}>
      <div className='modal' onClick={(e) => e.stopPropagation()}>
        <div className='modal-head'>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
            <div>
              <div className='modal-title'>{title}</div>
              {sub && <div className='modal-sub'>{sub}</div>}
            </div>
            <button className='icon-btn' onClick={onClose} aria-label='Fechar'>
              <X />
            </button>
          </div>
        </div>
        <div className='modal-body'>{children}</div>
        <div className='modal-foot'>{footer}</div>
      </div>
    </div>
  );
}

/* ---------- Drawer (painel lateral deslizante) ---------- */
export function Drawer({
  open,
  title,
  sub,
  onClose,
  footer,
  children,
  width = 480,
}: {
  open: boolean;
  title: string;
  sub?: string;
  onClose: () => void;
  footer?: ReactNode;
  children: ReactNode;
  width?: number;
}) {
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  if (!open) return null;
  return (
    <div className='drawer-overlay' onClick={onClose}>
      <aside className='drawer' style={{ width }} onClick={(e) => e.stopPropagation()} role='dialog' aria-label={title}>
        <div className='drawer-head'>
          <div>
            <div className='drawer-title'>{title}</div>
            {sub && <div className='drawer-sub'>{sub}</div>}
          </div>
          <button className='icon-btn' onClick={onClose} aria-label='Fechar'>
            <X />
          </button>
        </div>
        <div className='drawer-body'>{children}</div>
        {footer && <div className='drawer-foot'>{footer}</div>}
      </aside>
    </div>
  );
}

/* ---------- ConfirmDialog ---------- */
export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'Confirmar',
  cancelLabel = 'Cancelar',
  danger,
  busy,
  onConfirm,
  onCancel,
}: {
  title: string;
  message: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <div className='modal-overlay' onClick={onCancel}>
      <div className='modal confirm-modal' onClick={(e) => e.stopPropagation()} role='alertdialog' aria-label={title}>
        <div className='confirm-body'>
          {danger && (
            <div className='confirm-icon'>
              <AlertTriangle />
            </div>
          )}
          <div>
            <div className='modal-title'>{title}</div>
            <div className='confirm-msg'>{message}</div>
          </div>
        </div>
        <div className='modal-foot'>
          <Button onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </Button>
          <button className={cx('btn', danger ? 'btn-danger' : 'btn-brand')} onClick={onConfirm} disabled={busy}>
            {busy ? 'Aguarde…' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

/* ---------- Skeleton ---------- */
export function Skeleton({
  width,
  height = 14,
  className,
}: {
  width?: number | string;
  height?: number | string;
  className?: string;
}) {
  return <span className={cx('skeleton', className)} style={{ width, height, display: 'inline-block' }} />;
}

/** Linhas de skeleton para tabelas. */
export function SkeletonRows({ rows = 4, cols = 3 }: { rows?: number; cols?: number }) {
  return (
    <>
      {Array.from({ length: rows }).map((_, r) => (
        <tr key={r}>
          {Array.from({ length: cols }).map((__, c) => (
            <td key={c}>
              <Skeleton width={c === 0 ? '70%' : '45%'} />
            </td>
          ))}
        </tr>
      ))}
    </>
  );
}

/* ---------- MaskedInput ---------- */
type MaskedInputProps = {
  value: string;
  mask: (raw: string) => string;
  onValueChange: (masked: string) => void;
} & Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange'>;

export function MaskedInput({ value, mask, onValueChange, className, ...p }: MaskedInputProps) {
  return (
    <input
      className={cx('input', className)}
      value={mask(value)}
      onChange={(e) => onValueChange(mask(e.target.value))}
      {...p}
    />
  );
}

/** Hook de salvamento com estado de loading + erro. */
export function useSave(fn: () => Promise<void>) {
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);
  const run = async () => {
    setErro(null);
    setSalvando(true);
    try {
      await fn();
    } catch (e) {
      setErro(String(e));
      setSalvando(false);
    }
  };
  return { salvando, erro, run };
}
