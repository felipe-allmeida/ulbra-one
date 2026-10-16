import { cx } from '../../ui';

/** KPI tile for the `.kpitiles` 2×2 grid (mock "bgrid/ktile"). */
export function Tile({ n, l, tone }: { n: string; l: string; tone?: 'warn' | 'bad' }) {
  return (
    <div className='ktile'>
      <div className={cx('kn', tone)}>{n}</div>
      <div className='kl'>{l}</div>
    </div>
  );
}

/** Consumption bar: a "used" (consumido) segment + a "queued" segment as a share of orçado;
 * turns red when the total exceeds the budget. */
export function Bar({
  orcado,
  used,
  queued,
  over,
  tall,
}: {
  orcado: number;
  used: number;
  queued: number;
  over: boolean;
  tall?: boolean;
}) {
  const pct = (v: number) => (orcado > 0 ? Math.max(0, Math.min(100, (v / orcado) * 100)) : 0);
  return (
    <div className={cx('mbar', tall && 'tall')}>
      <div className='mseg used' style={{ width: `${pct(used)}%` }} />
      <div className={cx('mseg', over ? 'over' : 'queued')} style={{ width: `${pct(queued)}%` }} />
    </div>
  );
}

/** Color legend for a consumption bar (mock ".blegend"). */
export function BarLegend({ items }: { items: { color: string; label: string }[] }) {
  return (
    <div className='blegend'>
      {items.map((i) => (
        <span key={i.label}>
          <i className='sw' style={{ background: i.color }} />
          {i.label}
        </span>
      ))}
    </div>
  );
}
