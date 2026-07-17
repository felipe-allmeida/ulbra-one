import {
  useEffect,
  useId,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type ReactNode,
  type InputHTMLAttributes,
  type SelectHTMLAttributes,
  type ButtonHTMLAttributes,
} from 'react';
import { X, AlertTriangle, ChevronsUpDown, Check, Upload } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import * as Dialog from '@radix-ui/react-dialog';
import * as AlertDialog from '@radix-ui/react-alert-dialog';
import * as Popover from '@radix-ui/react-popover';

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

/** Form field (modal): label on top, help text below. */
export function Field({ label, help, children }: { label: ReactNode; help?: string; children: ReactNode }) {
  return (
    <div className='form-field'>
      <label>{label}</label>
      {children}
      {help && <div className='help'>{help}</div>}
    </div>
  );
}

/** Toolbar field (compact uppercase label). */
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
export function Kpi({
  label,
  value,
  foot,
  onClick,
  selected,
}: {
  label: string;
  value: string;
  foot?: string;
  onClick?: () => void;
  selected?: boolean;
}) {
  const body = (
    <>
      <div className='kpi-label'>{label}</div>
      <div className='kpi-value'>{value}</div>
      {foot && <div className='kpi-foot'>{foot}</div>}
    </>
  );
  if (onClick) {
    return (
      <button
        type='button'
        className={cx('kpi-card', 'kpi-card--clickable', selected && 'kpi-card--selected')}
        aria-pressed={selected}
        onClick={onClick}
      >
        {body}
      </button>
    );
  }
  return <div className='kpi-card'>{body}</div>;
}

/* ---------- Empty state ---------- */
export function EmptyState({ icon, title, sub, action }: { icon: ReactNode; title: string; sub?: string; action?: ReactNode }) {
  return (
    <div className='empty-state'>
      {icon}
      <div className='t'>{title}</div>
      {sub && <div className='s'>{sub}</div>}
      {action && <div className='a'>{action}</div>}
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

/* ---------- Pager ---------- */
/**
 * Simple prev/next pager for paginated browse lists. Renders nothing when there
 * is at most one page. `page`/`totalPages` are 1-based.
 */
export function Pager({
  page,
  totalPages,
  onPage,
}: {
  page: number;
  totalPages: number;
  onPage: (page: number) => void;
}) {
  const { t } = useTranslation();
  if (totalPages <= 1) return null;
  return (
    <div className='pager'>
      <Button onClick={() => onPage(page - 1)} disabled={page <= 1}>
        {t('pagination.prev')}
      </Button>
      <span className='pager-info'>{t('pagination.pageOf', { page, totalPages })}</span>
      <Button onClick={() => onPage(page + 1)} disabled={page >= totalPages}>
        {t('pagination.next')}
      </Button>
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
  size = 'default',
}: {
  title: string;
  sub?: string;
  onClose: () => void;
  footer: ReactNode;
  children: ReactNode;
  /** 'wide' widens the dialog for content like tables that don't fit the default 440px. */
  size?: 'default' | 'wide';
}) {
  return (
    <Dialog.Root open onOpenChange={(o) => !o && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className='modal-overlay'>
          <Dialog.Content className={size === 'wide' ? 'modal modal-wide' : 'modal'} aria-describedby={undefined}>
            <div className='modal-head'>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: 8 }}>
                <div>
                  <Dialog.Title className='modal-title'>{title}</Dialog.Title>
                  {sub && <Dialog.Description className='modal-sub'>{sub}</Dialog.Description>}
                </div>
                <Dialog.Close asChild>
                  <button className='icon-btn' aria-label='Fechar'>
                    <X />
                  </button>
                </Dialog.Close>
              </div>
            </div>
            <div className='modal-body'>{children}</div>
            <div className='modal-foot'>{footer}</div>
          </Dialog.Content>
        </Dialog.Overlay>
      </Dialog.Portal>
    </Dialog.Root>
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
  return (
    <Dialog.Root open={open} onOpenChange={(o) => !o && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className='drawer-overlay'>
          <Dialog.Content className='drawer' style={{ width }} aria-describedby={undefined} asChild>
            <aside>
              <div className='drawer-head'>
                <div>
                  <Dialog.Title className='drawer-title'>{title}</Dialog.Title>
                  {sub && <Dialog.Description className='drawer-sub'>{sub}</Dialog.Description>}
                </div>
                <Dialog.Close asChild>
                  <button className='icon-btn' aria-label='Fechar'>
                    <X />
                  </button>
                </Dialog.Close>
              </div>
              <div className='drawer-body'>{children}</div>
              {footer && <div className='drawer-foot'>{footer}</div>}
            </aside>
          </Dialog.Content>
        </Dialog.Overlay>
      </Dialog.Portal>
    </Dialog.Root>
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
    <AlertDialog.Root open onOpenChange={(o) => !o && onCancel()}>
      <AlertDialog.Portal>
        <AlertDialog.Overlay className='modal-overlay'>
          <AlertDialog.Content className='modal confirm-modal'>
            <div className='confirm-body'>
              {danger && (
                <div className='confirm-icon'>
                  <AlertTriangle />
                </div>
              )}
              <div>
                <AlertDialog.Title className='modal-title'>{title}</AlertDialog.Title>
                <AlertDialog.Description className='confirm-msg' asChild>
                  <div>{message}</div>
                </AlertDialog.Description>
              </div>
            </div>
            <div className='modal-foot'>
              <AlertDialog.Cancel asChild>
                <Button disabled={busy}>{cancelLabel}</Button>
              </AlertDialog.Cancel>
              <AlertDialog.Action asChild>
                <button
                  className={cx('btn', danger ? 'btn-danger' : 'btn-brand')}
                  onClick={(e) => {
                    e.preventDefault();
                    onConfirm();
                  }}
                  disabled={busy}
                >
                  {busy ? 'Aguarde…' : confirmLabel}
                </button>
              </AlertDialog.Action>
            </div>
          </AlertDialog.Content>
        </AlertDialog.Overlay>
      </AlertDialog.Portal>
    </AlertDialog.Root>
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

/** Skeleton rows for tables. */
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

/* ---------- Combobox (searchable single-select) ---------- */
export interface ComboboxItem {
  id: string;
  label: string;
  sub?: string;
}

/**
 * Accessible, searchable single-select built on Radix Popover. Renders the
 * selected item's label in the trigger; typing in the search field filters the
 * list (locally, and optionally upstream via `onSearch`). Keyboard: arrows move
 * the active option, Enter selects it, Esc closes. The optional `footer` slot is
 * a sticky action at the bottom of the list (used for "+ Novo").
 */
export function Combobox({
  value,
  onChange,
  items,
  placeholder,
  searchPlaceholder,
  onSearch,
  loading,
  footer,
  emptyText,
  disabled,
  invalid,
}: {
  value: string | null;
  onChange: (id: string | null) => void;
  items: ComboboxItem[];
  placeholder?: string;
  searchPlaceholder?: string;
  onSearch?: (q: string) => void;
  loading?: boolean;
  footer?: ReactNode;
  emptyText?: string;
  disabled?: boolean;
  invalid?: boolean;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listId = useId();

  const selected = items.find((i) => i.id === value) ?? null;

  // Local filter on top of whatever the caller already filtered upstream.
  const filtered = query.trim()
    ? items.filter((i) => (i.label + ' ' + (i.sub ?? '')).toLowerCase().includes(query.trim().toLowerCase()))
    : items;

  // Keep the active index in range as the filtered list changes.
  useEffect(() => {
    setActive((a) => (filtered.length === 0 ? 0 : Math.min(a, filtered.length - 1)));
  }, [filtered.length]);

  const updateQuery = (q: string) => {
    setQuery(q);
    setActive(0);
    onSearch?.(q);
  };

  const choose = (id: string) => {
    onChange(id);
    setOpen(false);
    setQuery('');
    onSearch?.('');
  };

  const onKeyDown = (e: ReactKeyboardEvent) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setActive((a) => Math.min(a + 1, filtered.length - 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setActive((a) => Math.max(a - 1, 0));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const item = filtered[active];
      if (item) choose(item.id);
    }
  };

  return (
    <Popover.Root
      open={open}
      onOpenChange={(o) => {
        setOpen(o);
        if (!o) {
          setQuery('');
          onSearch?.('');
        }
      }}
    >
      <Popover.Trigger asChild>
        <button
          type='button'
          className={cx('combobox-trigger', invalid && 'input-err')}
          disabled={disabled}
          aria-haspopup='listbox'
        >
          <span className={cx('combobox-value', !selected && 'combobox-placeholder')}>
            {selected ? selected.label : (placeholder ?? t('combobox.placeholder'))}
          </span>
          <ChevronsUpDown className='combobox-caret' />
        </button>
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          className='combobox-panel'
          align='start'
          sideOffset={4}
          onOpenAutoFocus={(e) => {
            e.preventDefault();
            inputRef.current?.focus();
          }}
        >
          <div className='combobox-search'>
            <input
              ref={inputRef}
              className='combobox-input'
              value={query}
              placeholder={searchPlaceholder ?? t('combobox.search')}
              onChange={(e) => updateQuery(e.target.value)}
              onKeyDown={onKeyDown}
              role='combobox'
              aria-expanded={open}
              aria-controls={listId}
              aria-autocomplete='list'
            />
          </div>
          <div className='combobox-list' id={listId} role='listbox'>
            {loading ? (
              <div className='combobox-empty'>{t('combobox.loading')}</div>
            ) : filtered.length === 0 ? (
              <div className='combobox-empty'>{emptyText ?? t('combobox.empty')}</div>
            ) : (
              filtered.map((item, idx) => (
                <button
                  type='button'
                  key={item.id}
                  role='option'
                  aria-selected={item.id === value}
                  className={cx('combobox-option', idx === active && 'active', item.id === value && 'selected')}
                  onMouseEnter={() => setActive(idx)}
                  onClick={() => choose(item.id)}
                >
                  <span className='combobox-option-main'>
                    <span className='combobox-option-label'>{item.label}</span>
                    {item.sub && <span className='combobox-option-sub'>{item.sub}</span>}
                  </span>
                  {item.id === value && <Check className='combobox-check' />}
                </button>
              ))
            )}
          </div>
          {footer && <div className='combobox-foot'>{footer}</div>}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
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

/** Save hook with loading + error state. */
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

/* ---------- FilePicker ---------- */
/** Styled file input: upload icon + the selected file name, or a placeholder when empty. */
export function FilePicker({
  accept,
  fileName,
  placeholder,
  onSelect,
  large,
}: {
  accept: string;
  fileName?: string | null;
  placeholder: string;
  onSelect: (f: File | null) => void;
  large?: boolean;
}) {
  return (
    <label className={large ? 'file-picker file-picker-lg' : 'file-picker'}>
      <Upload />
      <span className={fileName ? 'file-name' : undefined}>{fileName ?? placeholder}</span>
      <input
        type='file'
        accept={accept}
        style={{ display: 'none' }}
        onChange={(e) => {
          onSelect(e.target.files?.[0] ?? null);
          // Reset the input so re-selecting the SAME file fires onChange again (e.g. load → clear → reload same NF).
          e.target.value = '';
        }}
      />
    </label>
  );
}
