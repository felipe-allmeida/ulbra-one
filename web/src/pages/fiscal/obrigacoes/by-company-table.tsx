import { useTranslation } from 'react-i18next';
import type { CompanyAssessmentRow } from '../../../api';
import { brlCents } from '../../../lib';
import { Panel } from '../../../ui';
import { ResponsiveTable, type Column } from '../../../components/ResponsiveTable';

/** "Por empresa/estabelecimento": dimensão fiscal (ideEstab). Clicar abre os títulos daquela empresa. */
export function ByCompanyTable({
  rows,
  onDrill,
}: {
  rows: CompanyAssessmentRow[];
  onDrill: (row: CompanyAssessmentRow) => void;
}) {
  const { t } = useTranslation();

  // Ordem = a ordem já renderizada hoje. company é a única `title` (o botão que abre o drill-down — não há
  // onRowClick, o clique é no botão). cnpj é `meta`. withheld é o único `amount` (já em negrito hoje);
  // base/count são secundários → `detail`.
  const columns: Column<CompanyAssessmentRow>[] = [
    {
      key: 'company',
      header: t('fiscal.obrigacoes.columns.company'),
      role: 'title',
      cell: (r) => (
        <button type='button' className='linklike' onClick={() => onDrill(r)}>
          {r.companyName}
        </button>
      ),
    },
    {
      key: 'cnpj',
      header: t('fiscal.obrigacoes.columns.cnpj'),
      role: 'meta',
      cell: (r) => <span className='t-mono'>{r.cnpj}</span>,
    },
    {
      key: 'base',
      header: t('fiscal.obrigacoes.columns.base'),
      role: 'detail',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.baseAmount)}</span>,
    },
    {
      key: 'withheld',
      header: t('fiscal.obrigacoes.columns.withheld'),
      role: 'amount',
      align: 'right',
      cell: (r) => (
        <span className='amount-cell' style={{ fontWeight: 600 }}>
          {brlCents(r.withheldAmount)}
        </span>
      ),
    },
    {
      key: 'count',
      header: t('fiscal.obrigacoes.columns.count'),
      role: 'detail',
      align: 'right',
      cell: (r) => r.payableCount,
    },
  ];

  return (
    <div style={{ marginTop: 16 }}>
      <Panel title={t('fiscal.obrigacoes.byCompany')}>
        <ResponsiveTable rows={rows} columns={columns} rowKey={(r) => r.companyId} />
      </Panel>
    </div>
  );
}
