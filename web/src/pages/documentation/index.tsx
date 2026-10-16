import { Fragment, useEffect, useRef, type CSSProperties, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { Info, AlertTriangle, Ban, CheckCircle2, ChevronRight, ArrowRight, Wrench, Wallet } from 'lucide-react';
import { EmptyState, PageHeader, Panel, cx } from '../../ui';
import { StageTabBar, type StageTab } from '../../components/workflow';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { canSeeFinanceManual, useEffectiveMe } from '../../permissions';
import {
  DOC_SECTIONS,
  DOC_LABELS,
  type Block,
  type CalloutTone,
  type Deviation,
  type DocStep,
  type FlowNode,
  type RoleCard,
} from './content';

/**
 * User manual ("Documentação"): a left table-of-contents + a content pane, mirroring the legacy
 * manual's structure but built with the One's tokens/primitives (dark-mode correct, no Tailwind
 * utilities). Content comes from `content.tsx`; this file is the renderer + layout. Static, no fetch.
 */
export function DocumentationPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { sectionId } = useParams();
  const me = useEffectiveMe();
  // Restricted sections (e.g. the finance CNAB manual) only appear for the allowed cargos.
  const sections = DOC_SECTIONS.filter((s) => !s.gated || (s.gated === 'financeManual' && canSeeFinanceManual(me)));
  const active = sections.find((s) => s.id === sectionId) ?? sections[0];
  const topRef = useRef<HTMLDivElement>(null);

  // Switching sections (via the URL) while scrolled down should land at the top of the new content.
  useEffect(() => {
    topRef.current?.scrollIntoView({ block: 'start', behavior: 'auto' });
  }, [active.id]);

  return (
    <>
      <div ref={topRef} />
      <PageHeader title={t('nav.documentation')} sub={DOC_LABELS.pageSubtitle} />
      <div className='doc-layout'>
        <nav className='doc-toc' aria-label={t('nav.documentation')}>
          {sections.map((s) => (
            <button
              key={s.id}
              type='button'
              className={cx('doc-toc-item', s.id === active.id && 'active')}
              aria-current={s.id === active.id}
              onClick={() => void navigate(`/app/documentation/${s.id}`)}
            >
              <span className='doc-toc-ico'>{s.icon}</span>
              <span className='doc-toc-text'>
                <span className='doc-toc-label'>{s.navLabel}</span>
                <span className='doc-toc-sub'>{s.navSub}</span>
              </span>
              <ChevronRight className='doc-toc-chev' aria-hidden />
            </button>
          ))}
        </nav>

        <div className='doc-content'>
          <Panel title={active.title} padded>
            <p className='doc-lead'>{active.sub}</p>
            {active.blocks.map((block, i) => (
              <BlockView key={i} block={block} />
            ))}
          </Panel>
        </div>
      </div>
    </>
  );
}

/* --------------------------------------------------------------- helpers */

/** Inline emphasis: **bold** spans become <strong>; everything else stays plain. */
function rich(text: string): ReactNode {
  return text
    .split('**')
    .map((part, i) => (i % 2 === 1 ? <strong key={i}>{part}</strong> : <Fragment key={i}>{part}</Fragment>));
}

/** Accent color for a workflow stage (1..6); anything else (e.g. the terminal node) is neutral. */
const stageVar = (stage: number): string =>
  stage >= 1 && stage <= 6 ? `var(--doc-stage-${stage})` : 'var(--muted-foreground)';

const CALLOUT_ICON: Record<CalloutTone, ReactNode> = {
  info: <Info />,
  warning: <AlertTriangle />,
  danger: <Ban />,
  success: <CheckCircle2 />,
};

/* --------------------------------------------------------------- finance screen previews */

/** The four Gestão Financeira tabs, labels pulled from the real i18n so the preview matches the app. */
function FinanceTabs({ active }: { active: string }) {
  const { t } = useTranslation();
  const tabs: StageTab[] = [
    { key: 'queue', label: t('treasury.home.tabs.queue') },
    { key: 'remittances', label: t('treasury.home.tabs.remittances') },
    { key: 'cnabReturn', label: t('treasury.home.tabs.cnabReturn') },
    { key: 'history', label: t('treasury.home.tabs.history') },
  ];
  // Illustration only — the CSS makes it non-interactive (pointer-events: none), so onChange is a no-op.
  return <StageTabBar tabs={tabs} active={active} onChange={() => undefined} />;
}

/**
 * A "screen snippet" framed like a window, showing REAL app components (not a bitmap screenshot):
 * the Gestão Financeira tab bar, or the "Selecione uma empresa" empty state. Stays accurate and
 * theme-aware because it renders the same StageTabBar/EmptyState the screen uses.
 */
function FinanceScreen({
  variant,
  active,
  caption,
}: {
  variant: 'tabs' | 'selectCompany';
  active?: string;
  caption?: string;
}) {
  const { t } = useTranslation();
  return (
    <figure className='doc-screen-fig'>
      <div className='doc-screen'>
        <div className='doc-screen-bar'>
          <span className='doc-screen-dots'>
            <i className='doc-screen-dot' />
            <i className='doc-screen-dot' />
            <i className='doc-screen-dot' />
          </span>
          <span className='doc-screen-label'>{t('treasury.home.title')}</span>
        </div>
        <div className='doc-screen-body'>
          {variant === 'selectCompany' ? (
            <EmptyState
              icon={<Wallet />}
              title={t('treasury.home.noCompanyTitle')}
              sub={t('treasury.home.noCompanySub')}
            />
          ) : (
            <FinanceTabs active={active ?? 'queue'} />
          )}
        </div>
      </div>
      {caption && <figcaption className='doc-screen-caption'>{caption}</figcaption>}
    </figure>
  );
}

/* --------------------------------------------------------------- block renderer */

function BlockView({ block }: { block: Block }) {
  switch (block.kind) {
    case 'p':
      return <p className='doc-p'>{rich(block.text)}</p>;
    case 'heading':
      return <h3 className='doc-heading'>{block.text}</h3>;
    case 'list': {
      const items = block.items.map((it, i) => <li key={i}>{rich(it)}</li>);
      return block.ordered ? <ol className='doc-list doc-list-ol'>{items}</ol> : <ul className='doc-list'>{items}</ul>;
    }
    case 'callout':
      return (
        <div className={cx('doc-callout', `doc-tone-${block.tone}`)}>
          <span className='doc-callout-ico'>{CALLOUT_ICON[block.tone]}</span>
          <div>
            <div className='doc-callout-title'>{block.title}</div>
            <p className='doc-callout-text'>{rich(block.text)}</p>
          </div>
        </div>
      );
    case 'todo':
      return (
        <div className='doc-todo'>
          <span className='doc-todo-ico'>
            <Wrench />
          </span>
          <div>
            <span className='doc-todo-badge'>{DOC_LABELS.todoBadge}</span>
            <p className='doc-todo-text'>{rich(block.text)}</p>
          </div>
        </div>
      );
    case 'flow':
      return <FlowDiagram nodes={block.nodes} />;
    case 'deviations':
      return <Deviations items={block.items} />;
    case 'roles':
      return <Roles items={block.items} />;
    case 'steps':
      return (
        <div className='doc-steps'>
          {block.steps.map((s) => (
            <StepCard key={s.n} step={s} />
          ))}
        </div>
      );
    case 'table':
      return <DocTable head={block.head} rows={block.rows} />;
    case 'cards':
      return (
        <div className='doc-cats-wrap'>
          <span className='doc-cats-caption'>{block.caption}</span>
          <div className='doc-cats'>
            {block.items.map((c, i) => (
              <div key={i} className='doc-cat'>
                <span className='doc-cat-emoji'>{c.emoji}</span>
                <span className='doc-cat-title'>{c.title}</span>
              </div>
            ))}
          </div>
        </div>
      );
    case 'screen':
      return <FinanceScreen variant={block.variant} active={block.active} caption={block.caption} />;
  }
}

/* --------------------------------------------------------------- specialized blocks */

/** One row per glossary entry, keyed by its own index (static content, no natural id). */
interface DocTableRow {
  i: number;
  cells: string[];
}

/**
 * Generic head/rows glossary table (status → o que significa, and similar reference tables scattered
 * through the manual). The first column is always the entry being defined — the only `title` — and
 * every other head column (1 today, up to 2 seen in the content) is `detail`: plain reference text, no
 * row identity beyond its own position, nothing interactive — `detail` is the safe default the primitive
 * docs recommend when in doubt, and keeps arbitrarily long "o que fazer" text from truncating silently
 * the way a `meta`/`title` column would (ellipsis only shows for genuinely single-line content).
 */
function DocTable({ head, rows }: { head: string[]; rows: string[][] }) {
  const tableRows: DocTableRow[] = rows.map((cells, i) => ({ i, cells }));
  const columns: Column<DocTableRow>[] = head.map((h, col) => ({
    key: `col-${col}`,
    header: h,
    role: col === 0 ? 'title' : 'detail',
    cell: (r) => (col === 0 ? <strong>{r.cells[col]}</strong> : r.cells[col]),
  }));
  return (
    <div className='doc-table-wrap'>
      <ResponsiveTable rows={tableRows} columns={columns} rowKey={(r) => String(r.i)} />
    </div>
  );
}

function FlowDiagram({ nodes }: { nodes: FlowNode[] }) {
  return (
    // Planilha (grade numérica), não candidata a ResponsiveTable: exceção declarada — ver .table-scroll em index.css.
    <div className='doc-flow-wrap table-scroll'>
      <div className='doc-flow'>
        {nodes.map((node, i) => (
          <Fragment key={node.n}>
            <div className='doc-node' style={{ '--stage-color': stageVar(node.stage) } as CSSProperties}>
              <span className='doc-node-circle'>{node.n}</span>
              <span className='doc-node-title'>{node.title}</span>
              <span className='doc-node-role'>{node.role}</span>
            </div>
            {i < nodes.length - 1 && <ArrowRight className='doc-node-sep' aria-hidden />}
          </Fragment>
        ))}
      </div>
    </div>
  );
}

function Deviations({ items }: { items: Deviation[] }) {
  return (
    <div className='doc-deviations'>
      {items.map((d, i) => (
        <div key={i} className={cx('doc-deviation', `doc-tone-${d.tone}`)}>
          <div className='doc-deviation-head'>
            <span className='doc-deviation-ico'>{CALLOUT_ICON[d.tone]}</span>
            <span className='doc-deviation-title'>{d.title}</span>
          </div>
          <p className='doc-deviation-text'>{rich(d.text)}</p>
          <div className='doc-deviation-flow'>
            <span>{d.from}</span>
            <ArrowRight aria-hidden />
            <span>{d.to}</span>
          </div>
        </div>
      ))}
    </div>
  );
}

function Roles({ items }: { items: RoleCard[] }) {
  return (
    <div className='doc-roles'>
      {items.map((r) => (
        <div key={r.name} className='doc-role'>
          <div className='doc-role-head'>
            <span className='doc-role-name'>{r.name}</span>
            <span className='doc-role-summary'>{r.summary}</span>
          </div>
          <ul className='doc-role-points'>
            {r.points.map((p, i) => (
              <li key={i}>{rich(p)}</li>
            ))}
          </ul>
        </div>
      ))}
    </div>
  );
}

function StepCard({ step }: { step: DocStep }) {
  return (
    <div className='doc-step' style={{ '--stage-color': stageVar(step.stage) } as CSSProperties}>
      <div className='doc-step-head'>
        <div className='doc-step-heading'>
          <span className='doc-step-eyebrow'>{step.eyebrow}</span>
          <span className='doc-step-title'>
            <span className='doc-step-ico'>{step.icon}</span>
            {step.title}
          </span>
        </div>
        <span className='doc-step-status'>{step.status}</span>
      </div>

      <div className='doc-step-meta'>
        {step.meta.map((m, i) => (
          <div key={i} className='doc-meta-cell'>
            <span className='doc-meta-label'>{m.label}</span>
            <span className='doc-meta-value'>{m.value}</span>
          </div>
        ))}
      </div>

      <div className='doc-step-block'>
        <h4 className='doc-step-subtitle'>{DOC_LABELS.doTitle}</h4>
        <ul className='doc-check'>
          {step.doItems.map((it, i) => (
            <li key={i}>{it}</li>
          ))}
        </ul>
      </div>

      {step.rules.length > 0 && (
        <div className='doc-step-block doc-rules'>
          <h4 className='doc-step-subtitle'>{DOC_LABELS.rulesTitle}</h4>
          <ul className='doc-rules-list'>
            {step.rules.map((r, i) => (
              <li key={i}>
                <strong>{r.label}:</strong> {rich(r.text)}
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}
