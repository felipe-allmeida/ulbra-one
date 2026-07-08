import { useTranslation } from 'react-i18next';
import type { CompanyAssessmentRow } from '../../../api';
import { brlCents } from '../../../lib';
import { Panel } from '../../../ui';

/** "Por empresa/estabelecimento": dimensão fiscal (ideEstab). Clicar abre os títulos daquela empresa. */
export function ByCompanyTable({
  rows,
  onDrill,
}: {
  rows: CompanyAssessmentRow[];
  onDrill: (row: CompanyAssessmentRow) => void;
}) {
  const { t } = useTranslation();
  return (
    <div style={{ marginTop: 16 }}>
      <Panel title={t('fiscal.obrigacoes.byCompany')}>
        <table>
          <thead>
            <tr>
              <th>{t('fiscal.obrigacoes.columns.company')}</th>
              <th>{t('fiscal.obrigacoes.columns.cnpj')}</th>
              <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.base')}</th>
              <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.withheld')}</th>
              <th style={{ textAlign: 'right' }}>{t('fiscal.obrigacoes.columns.count')}</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.companyId}>
                <td>
                  <button type='button' className='linklike' onClick={() => onDrill(r)}>
                    {r.companyName}
                  </button>
                </td>
                <td className='t-mono'>{r.cnpj}</td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brlCents(r.baseAmount)}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                  {brlCents(r.withheldAmount)}
                </td>
                <td style={{ textAlign: 'right' }}>{r.payableCount}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
    </div>
  );
}
