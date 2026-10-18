import { useState, type ReactNode } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { cx } from '../../ui';

/** Expandable row (mock "expItem"): clickable header with title/meta/value/flag + inline body.
 * Used in the Approver Budgets screens (by nature and by project). */
export function ExpandableRow({
  title,
  meta,
  value,
  flag,
  risk,
  number,
  defaultOpen,
  onEnter,
  children,
}: {
  title: string;
  meta?: string;
  value?: string;
  flag?: { text: string; tone: 'ok' | 'warn' | 'bad' | 'calm' };
  /** Risk stripe on the left border (mock "rl"). */
  risk?: 'warn' | 'bad';
  /** Short identifier chip shown before the title (e.g. a requisition code "#42"). */
  number?: string;
  defaultOpen?: boolean;
  /** When given, clicking the header ENTERS the item instead of expanding it — the pattern every table in the
   *  app already follows. Rows without it keep the expand/collapse behaviour. */
  onEnter?: () => void;
  children: ReactNode;
}) {
  const [open, setOpen] = useState(defaultOpen ?? false);
  const entering = onEnter !== undefined;
  return (
    <div className='xrow'>
      <button
        type='button'
        className='xrow-head'
        aria-expanded={entering ? undefined : open}
        onClick={() => (entering ? onEnter() : setOpen((o) => !o))}
      >
        {risk && (
          <span className='rq-row-stripe' style={{ background: `var(--${risk === 'bad' ? 'danger' : 'accent'})` }} />
        )}
        <span className='rq-row-main xrow-main'>
          <span className='xrow-titleline'>
            {number && <span className='xrow-num'>{number}</span>}
            <span className='rq-row-title'>{title}</span>
          </span>
          {meta && <span className='rq-row-meta'>{meta}</span>}
        </span>
        <span className='rq-row-right'>
          {value && <span className='rq-row-value'>{value}</span>}
          {flag && <span className={cx('xrow-flag', flag.tone)}>{flag.text}</span>}
        </span>
        {/* A chevron promises "expands here"; in enter mode it would be a lie. */}
        {entering ? (
          <ChevronRight size={16} className='rq-row-chevron xrow-chevron' />
        ) : (
          <ChevronDown size={16} className={cx('rq-row-chevron', 'xrow-chevron', open && 'open')} />
        )}
      </button>
      {!entering && open && <div className='xrow-body'>{children}</div>}
    </div>
  );
}
