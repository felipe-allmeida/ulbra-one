import { Skeleton } from '../../ui';

/**
 * Skeleton para listas de requisição (rq-list/rq-row) enquanto o GET carrega — espelha o layout das
 * linhas em vez de piscar o estado vazio. `label` mostra um placeholder de rótulo de seção (abas com
 * seções); sem `label` é uma lista plana (aba "Precisa de você").
 */
export function RqListSkeleton({ rows = 4, label = false }: { rows?: number; label?: boolean }) {
  return (
    <section className='rq-section'>
      {label && (
        <div className='rq-seclabel'>
          <Skeleton width={150} height={12} />
        </div>
      )}
      <div className='rq-list'>
        {Array.from({ length: rows }).map((_, r) => (
          <div className='rq-row' key={r} style={{ cursor: 'default' }}>
            <div className='rq-row-main' style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              <Skeleton width='55%' />
              <Skeleton width={90} height={10} />
            </div>
            <div className='rq-row-right'>
              <Skeleton width={80} />
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
