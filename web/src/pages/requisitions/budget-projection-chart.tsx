import { brl } from '../../lib';
import { BarLegend } from './budget-tiles';
import { fmtCompetency } from './competency';

/** Ponto mensal: teto (orçado) × consumo projetado (consumido + fila/pedido, se aprovar). */
export interface ProjectionPoint {
  competency: string;
  ceiling: number;
  consumed: number;
  budgeted: boolean;
}

interface Props {
  points: ProjectionPoint[];
  /** Competências em estouro (autoritativo do servidor) — o consumo projetado passa do teto. */
  breaches: string[];
  ariaLabel: string;
  legend: { ceiling: string; consumption: string; over: string };
}

const W = 302;
const H = 124;
const PAD_L = 8;
const PAD_R = 8;
const PAD_T = 12;
const PAD_B = 18;
const PLOT_BOTTOM = H - PAD_B;

/**
 * Sparkline SVG do impacto orçamentário no modelo "consumo × teto" (AEL-876): o consumo projetado
 * (consumido + fila/pedido) SOBE como linha sólida + área; o teto orçado é a linha de referência
 * tracejada. Estouro = o consumo passa ACIMA do teto (faixa vermelha recortada + marcador vermelho).
 * Nunca recalcula estouro no cliente — usa só `breaches`. Puro/apresentacional; < 2 pontos → null.
 */
export function BudgetProjectionChart({ points, breaches, ariaLabel, legend }: Props) {
  if (points.length < 2) return null;
  const breachSet = new Set(breaches);

  const ceilings = points.map((p) => p.ceiling);
  const consumptions = points.map((p) => p.consumed);
  const rawMax = Math.max(...consumptions, ...ceilings, 0);
  const max = rawMax + (rawMax || 1) * 0.08;
  const min = 0;
  const range = max - min || 1;

  const x = (i: number) => PAD_L + (i / (points.length - 1)) * (W - PAD_L - PAD_R);
  const y = (v: number) => PAD_T + (1 - (v - min) / range) * (PLOT_BOTTOM - PAD_T);
  const baseY = y(0);

  // Runs de meses orçados consecutivos: o teto (linha de referência) quebra em meses sem orçamento.
  const runs: number[][] = [];
  let current: number[] | undefined;
  let prev = -2;
  points.forEach((p, i) => {
    if (!p.budgeted) {
      current = undefined;
      return;
    }
    if (current !== undefined && i === prev + 1) current.push(i);
    else {
      current = [i];
      runs.push(current);
    }
    prev = i;
  });

  // Consumo projetado: linha contínua + área da base até a linha.
  const consumptionLine = points
    .map((p, i) => `${i === 0 ? 'M' : 'L'} ${x(i).toFixed(1)} ${y(p.consumed).toFixed(1)}`)
    .join(' ');
  const consumptionArea =
    `${consumptionLine} L ${x(points.length - 1).toFixed(1)} ${baseY.toFixed(1)} ` +
    `L ${x(0).toFixed(1)} ${baseY.toFixed(1)} Z`;

  const ceilingLine = (run: number[]) =>
    run.map((i, k) => `${k === 0 ? 'M' : 'L'} ${x(i).toFixed(1)} ${y(points[i].ceiling).toFixed(1)}`).join(' ');
  // Região ACIMA do teto (até o topo do plot), por run: interseção com a área de consumo = o estouro.
  const aboveTeto = (run: number[]) =>
    `${ceilingLine(run)} L ${x(run[run.length - 1]).toFixed(1)} ${PAD_T} L ${x(run[0]).toFixed(1)} ${PAD_T} Z`;

  // Maior estouro (dentro do conjunto autoritativo do servidor) → rótulo "+R$ X".
  let overIdx = -1;
  let overVal = 0;
  for (let i = 0; i < points.length; i++) {
    if (!breachSet.has(points[i].competency)) continue;
    const d = points[i].consumed - points[i].ceiling;
    if (d > overVal) {
      overVal = d;
      overIdx = i;
    }
  }

  const labelStep = Math.ceil(points.length / 8);

  return (
    <div className='bpj-chart'>
      <svg viewBox={`0 0 ${W} ${H}`} width='100%' role='img' aria-label={ariaLabel} preserveAspectRatio='none'>
        <defs>
          <linearGradient id='bpj-area' x1='0' y1='0' x2='0' y2='1'>
            <stop offset='0%' stopColor='var(--brand)' stopOpacity={0.16} />
            <stop offset='100%' stopColor='var(--brand)' stopOpacity={0} />
          </linearGradient>
          <clipPath id='bpj-over'>
            {runs.map((run, i) => (
              <path key={i} d={aboveTeto(run)} />
            ))}
          </clipPath>
        </defs>

        {/* Área de consumo (herói) */}
        <path d={consumptionArea} fill='url(#bpj-area)' />
        {/* Estouro: mesma área de consumo, recortada para acima do teto */}
        <path d={consumptionArea} fill='var(--danger)' fillOpacity={0.18} clipPath='url(#bpj-over)' />

        {/* Teto orçado: linha de referência tracejada, quebrando em meses sem orçamento */}
        {runs.map((run, i) => (
          <path
            key={`t${i}`}
            d={ceilingLine(run)}
            fill='none'
            stroke='var(--foreground)'
            strokeOpacity={0.55}
            strokeWidth={1.4}
            strokeDasharray='5 3'
            strokeLinejoin='round'
            strokeLinecap='round'
          />
        ))}

        {/* Consumo projetado: linha sólida que sobe */}
        <path
          d={consumptionLine}
          fill='none'
          stroke='var(--brand)'
          strokeWidth={2.2}
          strokeLinejoin='round'
          strokeLinecap='round'
        />

        {/* Rótulo do maior estouro */}
        {overIdx >= 0 && overVal > 0 && (
          <text
            x={x(overIdx)}
            y={y(points[overIdx].consumed) - 5}
            fontSize={9.5}
            textAnchor='middle'
            fill='var(--danger)'
            fontWeight={700}
          >
            {`+${brl(overVal)}`}
          </text>
        )}

        {/* Vértices do teto (cheio / vazado em mês sem orçamento) */}
        {points.map((p, i) =>
          p.budgeted ? (
            <circle key={`c${i}`} cx={x(i)} cy={y(p.ceiling)} r={1.8} fill='var(--foreground)' fillOpacity={0.5} />
          ) : (
            <circle
              key={`c${i}`}
              cx={x(i)}
              cy={baseY}
              r={2.2}
              fill='var(--card)'
              stroke='var(--muted-foreground)'
              strokeWidth={1}
              data-testid='bpj-nobudget'
            />
          ),
        )}
        {/* Vértices do consumo (vermelho onde estoura = passa do teto) */}
        {points.map((p, i) =>
          breachSet.has(p.competency) ? (
            <circle
              key={`p${i}`}
              cx={x(i)}
              cy={y(p.consumed)}
              r={3.6}
              fill='var(--danger)'
              stroke='var(--card)'
              strokeWidth={1.4}
              data-testid='bpj-breach'
            />
          ) : (
            <circle key={`p${i}`} cx={x(i)} cy={y(p.consumed)} r={2.2} fill='var(--brand)' />
          ),
        )}

        {/* Rótulos X esparsos */}
        {points.map((p, i) =>
          i % labelStep === 0 ? (
            <text key={`x${i}`} x={x(i)} y={H - 5} fontSize={9} textAnchor='middle' fill='var(--muted-foreground)'>
              {fmtCompetency(p.competency)}
            </text>
          ) : null,
        )}
      </svg>

      <BarLegend
        items={[
          { color: 'var(--foreground)', label: legend.ceiling },
          { color: 'var(--brand)', label: legend.consumption },
          { color: 'var(--danger)', label: legend.over },
        ]}
      />
    </div>
  );
}
