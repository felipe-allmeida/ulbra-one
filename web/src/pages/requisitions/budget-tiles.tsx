import { cx } from '../../ui';
import { brlCents, brlKpi } from '../../lib';

/** KPI tile for the `.kpitiles` 2×2 grid (mock "bgrid/ktile"). */
export function Tile({ n, l, tone, title }: { n: string; l: string; tone?: 'warn' | 'bad'; title?: string }) {
  return (
    <div className='ktile'>
      <div className={cx('kn', tone)} title={title}>
        {n}
      </div>
      <div className='kl'>{l}</div>
    </div>
  );
}

/** Tile props for a money KPI: abbreviated from a million up so it never wraps mid-number in the 360px
 *  aside (`brlKpi`), with the exact value on hover. Spread it: `<Tile {...money(total)} l={…} />`. */
export const money = (v: number | null | undefined) => ({ n: brlKpi(v), title: brlCents(v) });

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
