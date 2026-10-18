import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { WithholdingFactRow } from '../../../api';
import { brlCents } from '../../../lib';
import { useOriginState } from '../../../nav';
import { Drawer } from '../../../ui';
import { ResponsiveTable, type Column } from '../../../components/ResponsiveTable';

interface ByPayableRow {
  payableId: string;
  counterpartyName: string;
  documentNumber: string | null;
  withheld: number;
}

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
  const originState = useOriginState();
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

  // Ordem = a ordem já renderizada hoje. supplier é a única `title`; document é `meta`; withheld é o único
  // `amount`. A última coluna é só o link "abrir título" — o <th> original era vazio, então `header: ''`
  // (Task 13 já corrigiu 4 regressões desse tipo). O total no rodapé é a soma das próprias `rows` (byPayable
  // já é o conjunto completo por trás desta apuração, sem paginação/filtro adicional).
  const columns: Column<ByPayableRow>[] = [
    {
      key: 'supplier',
      header: t('fiscal.obrigacoes.columns.supplier'),
      role: 'title',
      cell: (p) => p.counterpartyName,
    },
    {
      key: 'document',
      header: t('fiscal.obrigacoes.columns.document'),
      role: 'meta',
      cell: (p) => <span className='t-mono'>{p.documentNumber ?? '—'}</span>,
    },
    {
      key: 'withheld',
      header: t('fiscal.obrigacoes.columns.withheld'),
      role: 'amount',
      align: 'right',
      cell: (p) => <span className='amount-cell'>{brlCents(p.withheld)}</span>,
      footCell: (rs) => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {brlCents(rs.reduce((s, p) => s + p.withheld, 0))}
        </span>
      ),
    },
    {
      key: 'openPayable',
      header: '',
      role: 'detail',
      cell: (p) => (
        <Link className='linklike' to={`/app/payables/${p.payableId}`} state={originState}>
          {t('fiscal.obrigacoes.openPayable')}
        </Link>
      ),
    },
  ];

  return (
    <Drawer open onClose={onClose} title={t('fiscal.obrigacoes.drillTitle')} sub={drill.title}>
      <div className='drawer-table-scroll'>
        <ResponsiveTable
          rows={byPayable}
          columns={columns}
          rowKey={(p) => p.payableId}
          footLabel={<span style={{ fontWeight: 600 }}>{t('fiscal.obrigacoes.total')}</span>}
        />
      </div>
    </Drawer>
  );
}
