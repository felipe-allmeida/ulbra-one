import { type ReactNode } from 'react';
import { Gavel } from 'lucide-react';

/**
 * Painel de decisão do aside. Fica no topo da coluna sticky de 360px (ver `AsideLayout`), então
 * acompanha o scroll: a decisão continua visível mesmo com o histórico na tela.
 *
 * O painel não sabe de nenhum domínio — `context` e as ações vêm de quem o usa. Requisição traz saldo
 * de centro de custo; conta a pagar traz caixa ou vencimento. Foi por isso que ele saiu de
 * `pages/requisitions/`: o contexto é o que muda entre as telas, não a moldura.
 */
export function DecisionPanel({
  title,
  subtitle,
  context,
  children,
}: {
  title: string;
  subtitle?: string;
  /** Linhas de contexto acima das ações; use `DecisionRow`. Omita quando não há decisão a tomar. */
  context?: ReactNode;
  children: ReactNode;
}) {
  return (
    <div className='spanel spanel-decision'>
      <div className='sph'>
        <div className='spt'>
          <Gavel aria-hidden />
          {title}
        </div>
        {subtitle && <div className='sps'>{subtitle}</div>}
      </div>
      <div className='spbody'>
        {context && <div className='decision-context'>{context}</div>}
        <div className='decision-actions'>{children}</div>
      </div>
    </div>
  );
}

/** Linha label→valor do contexto da decisão. */
export function DecisionRow({ label, value, tone }: { label: string; value: string; tone?: 'warn' | 'bad' | 'muted' }) {
  return (
    <div className='decision-row'>
      <span className='k'>{label}</span>
      <span className={tone ? `v is-${tone}` : 'v'}>{value}</span>
    </div>
  );
}
