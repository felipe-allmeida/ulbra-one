import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { WithholdingFactRow } from '../../../api';
import { brlCents } from '../../../lib';
import { Drawer } from '../../../ui';

/** Drawer que mostra os títulos (payables) por trás de uma linha da apuração (empresa ou centro de custo),
 * com link para o título completo. As retenções vêm agregadas por título a partir dos fatos filtrados. */
export function AssessmentDrillDrawer({
  drill,
  onClose,
}: {
  drill: { title: string; facts: WithholdingFactRow[] } | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  if (!drill) return null;

  const byPayable = Object.values(
    drill.facts.reduce<
      Record<string, { payableId: string; counterpartyName: string; documentNumber: string | null; withheld: number }>
    >((acc, f) => {
      const cur = acc[f.payableId] ?? {
        payableId: f.payableId,
        counterpartyName: f.counterpartyName,
        documentNumber: f.documentNumber,
        withheld: 0,
      };
      cur.withheld += f.amount;
      acc[f.payableId] = cur;
      return acc;
    }, {}),
  ).sort((a, b) => b.withheld - a.withheld);

  const total = byPayable.reduce((s, p) => s + p.withheld, 0);

  return (
    <Drawer open onClose={onClose} title={t('fiscal.obrigacoes.drillTitle')} sub={drill.title}>
      <table>
        <thead>
          <tr>
            <th>{t('fiscal.obrigacoes.columns.supplier')}</th>
            <th>{t('fiscal.obrigacoes.columns.document')}</th>
            <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.withheld')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {byPayable.map((p) => (
            <tr key={p.payableId}>
              <td>{p.counterpartyName}</td>
              <td className='t-mono'>{p.documentNumber ?? '—'}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(p.withheld)}
              </td>
              <td>
                <Link className='linklike' to={`/app/payables/${p.payableId}`}>
                  {t('fiscal.obrigacoes.openPayable')}
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td style={{ fontWeight: 600 }}>{t('fiscal.obrigacoes.total')}</td>
            <td />
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(total)}
            </td>
            <td />
          </tr>
        </tfoot>
      </table>
    </Drawer>
  );
}
