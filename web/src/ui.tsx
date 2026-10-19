import {
  createContext,
  useContext,
  useEffect,
  useId,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent,
  type MouseEvent as ReactMouseEvent,
  type ReactNode,
  type InputHTMLAttributes,
  type SelectHTMLAttributes,
  type TextareaHTMLAttributes,
  type ButtonHTMLAttributes,
} from 'react';
import { X, AlertTriangle, ChevronsUpDown, Check, Upload, Loader2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import * as Dialog from '@radix-ui/react-dialog';
import * as AlertDialog from '@radix-ui/react-alert-dialog';
import * as Popover from '@radix-ui/react-popover';
import { parseMoneyInput } from './lib';
import { usePhone } from './useMediaQuery';

export const cx = (...c: (string | false | undefined | null)[]) => c.filter(Boolean).join(' ');

/* Popover portal target: Drawer/Modal expose their Dialog.Content node here so the Combobox/MultiSelect
   popover portals INSIDE the dialog's focus scope. Otherwise the modal focus-trap reclaims focus from the
   body-portaled search input and keystrokes are dropped ("won't let me type"). null (outside any dialog) →
   Radix defaults to document.body, so page-level usage is unchanged. */
const PopoverContainerContext = createContext<HTMLElement | null>(null);

/** Case- and accent-insensitive normalization for local combobox filtering (pt-BR data carries accents). */
const normalizeText = (s: string) =>
  s
    .toLowerCase()
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '');

/* ---------- Button ---------- */
type BtnProps = {
  variant?: 'default' | 'brand' | 'ghost' | 'danger' | 'danger-outline';
  icon?: ReactNode;
} & ButtonHTMLAttributes<HTMLButtonElement>;
export function Button({ variant = 'default', icon, children, className, type = 'button', ...p }: BtnProps) {
  return (
    // Default type='button': a bare <button> inside a <form> defaults to submit, so any non-submit Button would
    // accidentally submit (and navigate away). Form submit buttons opt in explicitly with type='submit'.
    <button
      type={type}
      className={cx(
        'btn',
        variant === 'brand' && 'btn-brand',
        variant === 'ghost' && 'btn-ghost',
        variant === 'danger' && 'btn-danger',
        variant === 'danger-outline' && 'btn-danger-outline',
        className,
      )}
      {...p}
    >
      {icon}
      {children}
    </button>
  );
}

/* ---------- Linha clicável ---------- */
/** Seletor dos elementos que "consomem" o clique dentro de uma linha clicável. */
const INTERACTIVE = 'button, a, input, select, textarea, label, [role="button"], [role="checkbox"]';

/**
 * Handler de `<tr className='row-click'>`: abre o detalhe, exceto quando o clique caiu num controle
 * de verdade (o botão de ação da linha, um checkbox de seleção).
 *
 * Substitui o `onClick={(e) => e.stopPropagation()}` que se punha na célula inteira — aquilo isolava
 * o botão, mas transformava todo o resto da célula em zona morta: o clique não abria o detalhe e
 * também não fazia mais nada. Aqui a exceção é o controle, não a célula.
 */
export function onRowClick(open: () => void) {
  return (e: ReactMouseEvent<HTMLElement>) => {
    if ((e.target as HTMLElement).closest(INTERACTIVE)) return;
    open();
  };
}

/* ---------- Field ↔ control binding ---------- */
/** Id emitido pelo `Field`/`ToolbarField` mais próximo. Os controles abaixo o adotam quando não
 * recebem `id` próprio — é o que liga o `<label htmlFor>` ao campo, então clicar no rótulo foca o
 * controle e o leitor de tela anuncia o nome. Um `id` explícito no call site sempre vence. */
const FieldIdContext = createContext<string | undefined>(undefined);
function useFieldId(explicit?: string) {
  const inherited = useContext(FieldIdContext);
  return explicit ?? inherited;
}

/* ---------- Inputs ---------- */
export function Input({ className, id, ...p }: InputHTMLAttributes<HTMLInputElement>) {
  const fieldId = useFieldId(id);
  return <input id={fieldId} className={cx('input', className)} {...p} />;
}
/** Multi-line text field — same `.input` styling as {@link Input}, but vertically resizable so longer
 * justificativas (devolução/cancelamento) ficam legíveis. Default 3 linhas. */
export function Textarea({ className, rows = 3, style, id, ...p }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  const fieldId = useFieldId(id);
  return (
    <textarea
      id={fieldId}
      className={cx('input', className)}
      rows={rows}
      style={{ resize: 'vertical', ...style }}
      {...p}
    />
  );
}
export function Select({ className, children, id, ...p }: SelectHTMLAttributes<HTMLSelectElement>) {
  const fieldId = useFieldId(id);
  return (
    <select id={fieldId} className={cx('select', className)} {...p}>
      {children}
    </select>
  );
}

/** Form field (modal): label on top, help text below. */
export function Field({
  label,
  help,
  required,
  htmlFor,
  children,
}: {
  label: ReactNode;
  help?: string;
  required?: boolean;
  /** Fixa o id do controle em vez de deixar o `Field` gerar um — para quando o call site já
   * precisa do id por outro motivo (âncora, `aria-describedby`, foco programático). */
  htmlFor?: string;
  children: ReactNode;
}) {
  const auto = useId();
  const id = htmlFor ?? auto;
  return (
    <div className='form-field'>
      <label htmlFor={id}>
        {label}
        {required && <span className='req-mark'> *</span>}
      </label>
      <FieldIdContext.Provider value={id}>{children}</FieldIdContext.Provider>
      {help && <div className='help'>{help}</div>}
    </div>
  );
}

/** Labeled read-only field (Field + disabled Input); `full` spans the whole grid row. */
export function ReadonlyField({ label, value, full }: { label: string; value: string; full?: boolean }) {
  return (
    <div style={full ? { gridColumn: '1 / -1' } : undefined}>
      <Field label={label}>
        <Input value={value} readOnly />
      </Field>
    </div>
  );
}

/** Toolbar field (compact uppercase label). */
export function ToolbarField({
  label,
  children,
  className,
}: {
  label: string;
  children: ReactNode;
  className?: string;
}) {
  const id = useId();
  return (
    <div className={className ? `field ${className}` : 'field'}>
      <label htmlFor={id}>{label}</label>
      <FieldIdContext.Provider value={id}>{children}</FieldIdContext.Provider>
    </div>
  );
}

/* ---------- Chip ---------- */
export function Chip({
  tone = 'default',
  icon,
  children,
}: {
  tone?: 'default' | 'brand' | 'auto' | 'warn';
  icon?: ReactNode;
  children: ReactNode;
}) {
  return (
    <span className={cx('chip', tone === 'brand' && 'all', tone === 'auto' && 'auto', tone === 'warn' && 'warn')}>
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
export function EmptyState({
  icon,
  title,
  sub,
  action,
}: {
  icon: ReactNode;
  title: string;
  sub?: string;
  action?: ReactNode;
}) {
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
/**
 * Titled card. The body has NO padding by default so tables sit flush (full-bleed).
 * For non-table content (forms, chips, bars) pass `padded` to inset the body.
 */
export function Panel({
  title,
  action,
  children,
  padded,
}: {
  title: string;
  action?: ReactNode;
  children: ReactNode;
  padded?: boolean;
}) {
  return (
    <div className='panel'>
      <div className='panel-head'>
        <span className='panel-title'>{title}</span>
        {action}
      </div>
      {padded ? <div className='panel-body'>{children}</div> : children}
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
  const [contentEl, setContentEl] = useState<HTMLElement | null>(null);
  return (
    <Dialog.Root open onOpenChange={(o) => !o && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className='modal-overlay'>
          <Dialog.Content
            ref={setContentEl}
            className={size === 'wide' ? 'modal modal-wide' : 'modal'}
            aria-describedby={undefined}
          >
            <PopoverContainerContext.Provider value={contentEl}>
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
            </PopoverContainerContext.Provider>
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
  const [contentEl, setContentEl] = useState<HTMLElement | null>(null);
  return (
    <Dialog.Root open={open} onOpenChange={(o) => !o && onClose()}>
      <Dialog.Portal>
        <Dialog.Overlay className='drawer-overlay'>
          <Dialog.Content className='drawer' style={{ width }} aria-describedby={undefined} asChild>
            <aside ref={setContentEl}>
              <PopoverContainerContext.Provider value={contentEl}>
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
              </PopoverContainerContext.Provider>
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
  confirmDisabled,
  onConfirm,
  onCancel,
}: {
  title: string;
  message: ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  busy?: boolean;
  /** Trava o confirmar enquanto uma condição do chamador não for satisfeita (ex.: motivo obrigatório). */
  confirmDisabled?: boolean;
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
                  disabled={busy || confirmDisabled}
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

/* ---------- Spinner & LoadingState ---------- */
/** Animated spinner reusing the shared `.spin` idiom. */
export function Spinner({ size = 18, className }: { size?: number; className?: string }) {
  return <Loader2 size={size} className={cx('spin', className)} aria-hidden />;
}

/** Centered loading block (spinner + label) for detail/report/form screens — the
 * non-table counterpart of SkeletonRows. Defaults the label to common.loading. */
export function LoadingState({ label, className }: { label?: string; className?: string }) {
  const { t } = useTranslation();
  return (
    <div className={cx('loading-state', className)} role='status' aria-live='polite'>
      <Spinner size={22} />
      <span>{label ?? t('common.loading')}</span>
    </div>
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
  const container = useContext(PopoverContainerContext);

  const selected = items.find((i) => i.id === value) ?? null;

  // Local filter on top of whatever the caller already filtered upstream. Case- and accent-insensitive.
  const filtered = query.trim()
    ? items.filter((i) => normalizeText(i.label + ' ' + (i.sub ?? '')).includes(normalizeText(query.trim())))
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
      <Popover.Portal container={container ?? undefined}>
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

/* ---------- MultiSelect ---------- */
export interface MultiSelectOption {
  value: string;
  label: string;
}

/**
 * Multi-select dropdown ("selectButtons"): a Select-looking trigger that opens a panel of toggle buttons;
 * clicking each toggles it in/out of `selected`. The trigger summarizes the selection ("Todos" / the single
 * label / "N selecionados"). Reuses the Combobox popover + styling. Empty selection = no filter (all).
 */
export function MultiSelect({
  options,
  selected,
  onChange,
  allLabel,
  countLabel,
  width,
}: {
  options: MultiSelectOption[];
  selected: string[];
  onChange: (values: string[]) => void;
  /** Shown on the trigger and as the "clear" action when nothing is selected (e.g. "Todas as unidades"). */
  allLabel: string;
  /** Builds the trigger summary for 2+ selected, e.g. (n) => `${n} selecionadas`. */
  countLabel: (n: number) => string;
  width?: number;
}) {
  const [open, setOpen] = useState(false);
  const container = useContext(PopoverContainerContext);
  const has = selected.length > 0;
  const toggle = (v: string) => onChange(selected.includes(v) ? selected.filter((x) => x !== v) : [...selected, v]);
  const summary = !has
    ? allLabel
    : selected.length === 1
      ? (options.find((o) => o.value === selected[0])?.label ?? countLabel(1))
      : countLabel(selected.length);
  // `width` is a desktop layout choice (fit N fields on one toolbar row) — inline, so it would
  // otherwise beat the `.combobox-trigger { width: 100% }` mobile reset below 640px (same trap as
  // the bare `.select`, Task 9). Drop it on phone instead of fighting the CSS with more inline style.
  const phone = usePhone();

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <button
          type='button'
          className={cx('combobox-trigger', has && 'has-value')}
          style={{ width: phone ? undefined : width }}
        >
          <span className={cx('combobox-value', !has && 'combobox-placeholder')}>{summary}</span>
          <ChevronsUpDown className='combobox-caret' />
        </button>
      </Popover.Trigger>
      <Popover.Portal container={container ?? undefined}>
        <Popover.Content className='combobox-panel' align='start' sideOffset={4}>
          <div className='combobox-list' role='listbox' aria-multiselectable='true'>
            {options.map((o) => {
              const on = selected.includes(o.value);
              return (
                <button
                  type='button'
                  key={o.value}
                  role='option'
                  aria-selected={on}
                  className={cx('combobox-option', on && 'selected')}
                  onClick={() => toggle(o.value)}
                >
                  <span className='combobox-option-main'>
                    <span className='combobox-option-label'>{o.label}</span>
                  </span>
                  {on && <Check className='combobox-check' />}
                </button>
              );
            })}
          </div>
          {has && (
            <div className='combobox-foot'>
              <button type='button' className='combobox-clear' onClick={() => onChange([])}>
                {allLabel}
              </button>
            </div>
          )}
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

export function MaskedInput({ value, mask, onValueChange, className, id, ...p }: MaskedInputProps) {
  const fieldId = useFieldId(id);
  return (
    <input
      id={fieldId}
      className={cx('input', className)}
      value={mask(value)}
      onChange={(e) => onValueChange(mask(e.target.value))}
      {...p}
    />
  );
}

/* ---------- CurrencyInput ---------- */
type CurrencyInputProps = {
  value: string;
  onValueChange: (raw: string) => void;
} & Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange'>;

/** Money field in pt-BR convention. Holds the raw string in form state; accepts digits, '.' and ','
 * while typing and normalizes to the canonical "1.234,56" on blur (grouping + 2 decimals). Parse the
 * stored string with `parseMoneyInput` at validation/submit time — a bare Number() would NaN on commas. */
export function CurrencyInput({
  value,
  onValueChange,
  onBlur,
  className,
  inputMode = 'decimal',
  placeholder = '0,00',
  id,
  ...p
}: CurrencyInputProps) {
  const fieldId = useFieldId(id);
  return (
    <input
      id={fieldId}
      className={cx('input', className)}
      inputMode={inputMode}
      placeholder={placeholder}
      value={value}
      onChange={(e) => onValueChange(e.target.value.replace(/[^\d.,-]/g, ''))}
      onBlur={(e) => {
        const n = parseMoneyInput(e.target.value);
        if (n !== null)
          onValueChange(n.toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }));
        onBlur?.(e);
      }}
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
/** Best-effort mirror of the `<input accept>` dialog filtering, for dropped files (a drop bypasses the
 *  dialog). Matches by extension (".pdf") or MIME ("application/pdf", "image/*"); empty accept allows all. */
function matchesAccept(file: File, accept: string): boolean {
  const tokens = accept
    .split(',')
    .map((s) => s.trim().toLowerCase())
    .filter(Boolean);
  if (tokens.length === 0) return true;
  const name = file.name.toLowerCase();
  const type = file.type.toLowerCase();
  return tokens.some((tok) => {
    if (tok.startsWith('.')) return name.endsWith(tok);
    if (tok.endsWith('/*')) return type.startsWith(tok.slice(0, -1)); // "image/*" → "image/"
    return type === tok;
  });
}

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
  const [dragging, setDragging] = useState(false);
  return (
    // Drag-and-drop: the label looks like a dropzone, so wire real drop handling. preventDefault on
    // dragover is what lets `drop` fire — without it the browser just opens the dropped file in the tab.
    <label
      className={cx('file-picker', large && 'file-picker-lg', dragging && 'file-picker-dragging')}
      onDragOver={(e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
      }}
      onDragEnter={(e) => {
        e.preventDefault();
        setDragging(true);
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={(e) => {
        e.preventDefault();
        setDragging(false);
        const f = e.dataTransfer.files?.[0];
        // Honour `accept` on drop the way the dialog does on click; ignore a wrong-typed drop.
        if (f && matchesAccept(f, accept)) onSelect(f);
      }}
    >
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
