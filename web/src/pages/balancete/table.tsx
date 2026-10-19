import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { TrialBalance, TrialBalanceRow } from '../../api';
import { brlCents } from '../../lib';
import { Panel } from '../../ui';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';

/** The balancete table: one row per account (código · cód. resumido · conta · saldo anterior · débitos ·
 *  créditos · saldo final) plus the period totals. The double-entry balancing badge (Σdébito = Σcrédito) is a
 *  valid proof only for the whole ledger — a cost-center slice filters by line, so its debit and credit legs
 *  usually fall under different (or no) cost centers and the slice legitimately does not balance. When a cost
 *  center is applied we therefore show a neutral "partial view" label instead of the balancing badge. */
export function BalanceteTable({
  report,
  from,
  to,
  costCenterId,
  unitId,
}: {
  report: TrialBalance;
  from: string;
  to: string;
  costCenterId: string;
  unitId: string;
}) {
  const { t } = useTranslation();
  // Drill-down into the Livro Razão for a single account, carrying over the current period/context. The Razão
  // filters by ledger-account id (its combobox stores the id and shows the code as label), so we pass the id.
  // The empresa/consolidado scope is not carried in the URL — it is the topbar's, shared by both screens.
  const ledgerHref = (ledgerAccountId: string) => {
    const qs = new URLSearchParams({ account: ledgerAccountId });
    if (from) qs.set('from', from);
    if (to) qs.set('to', to);
    if (costCenterId) qs.set('costCenterId', costCenterId);
    if (unitId) qs.set('unitId', unitId);
    return `/app/accounting/ledger?${qs.toString()}`;
  };
  // Ordem = a ordem já renderizada hoje no desktop (código, código reduzido, conta, saldo anterior, débito,
  // crédito, saldo final) — preserva o desktop pixel a pixel. accountName é a única `title` (identifica a
  // linha para um humano; os códigos identificam para o sistema). balance (saldo final) é o único `amount`
  // — o número que fecha a história da linha.
  //
  // débito e crédito também foram tentados como `meta` (visíveis sem expandir, já que um balancete é um
  // somatório e escondê-los apaga a história da linha) — mas `.rt-meta` é `white-space: nowrap` com
  // ellipsis e sem rótulo: com valores grandes o segundo número trunca ("R$ 123.456,00 · R$ 456.789,...")
  // e, mesmo sem truncar, nada diz qual dos dois é débito e qual é crédito. `detail` resolve os dois
  // problemas de graça — grid com rótulo (DÉBITOS/CRÉDITOS) e `overflow-wrap: anywhere` em vez de truncar.
  // Ver comparação com screenshots no relatório da task 11. Código, código reduzido e saldo anterior
  // seguem o mesmo raciocínio: `detail`, consultados sob demanda.
  const columns: Column<TrialBalanceRow>[] = [
    {
      key: 'code',
      header: t('accounting.trialBalance.columns.code'),
      role: 'detail',
      cell: (r) => (
        <Link className='linklike t-mono' to={ledgerHref(r.ledgerAccountId)}>
          {r.accountCode}
        </Link>
      ),
    },
    {
      key: 'reducedCode',
      header: t('accounting.trialBalance.columns.reducedCode'),
      role: 'detail',
      cell: (r) => <span className='t-mono'>{r.reducedCode ?? '—'}</span>,
    },
    { key: 'account', header: t('accounting.trialBalance.columns.account'), role: 'title', cell: (r) => r.accountName },
    {
      key: 'openingBalance',
      header: t('accounting.trialBalance.columns.openingBalance'),
      role: 'detail',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.openingBalance)}</span>,
    },
    // débito e crédito ignoram o parâmetro `rows` do footCell de propósito: o total "de verdade" vem
    // de report.totalDebit/report.totalCredit (calculados pela API sobre o período inteiro), não da
    // soma das linhas exibidas. Sob um filtro por centro de custo os dois legitimamente divergem — a
    // mesma razão pela qual o badge de balanceamento acima vira "visão parcial" nesse caso. Somar
    // `rows` aqui mostraria o total da página, não o total real do relatório.
    {
      key: 'debit',
      header: t('accounting.trialBalance.columns.debit'),
      role: 'detail',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.totalDebit)}</span>,
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {brlCents(report.totalDebit)}
        </span>
      ),
    },
    {
      key: 'credit',
      header: t('accounting.trialBalance.columns.credit'),
      role: 'detail',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.totalCredit)}</span>,
      footCell: () => (
        <span className='amount-cell' style={{ fontWeight: 700 }}>
          {brlCents(report.totalCredit)}
        </span>
      ),
    },
    {
      key: 'balance',
      header: t('accounting.trialBalance.columns.balance'),
      role: 'amount',
      align: 'right',
      cell: (r) => <span className='amount-cell'>{brlCents(r.balance)}</span>,
    },
  ];

  return (
    <Panel
      title={t('accounting.trialBalance.title')}
      action={
        report.costCenterId ? (
          <span className='badge-status'>{t('accounting.trialBalance.costCenterPartial')}</span>
        ) : (
          <span className={`badge-status ${report.isBalanced ? 'badge-aprovado' : 'badge-rejeitado'}`}>
            {report.isBalanced ? t('accounting.trialBalance.balanced') : t('accounting.trialBalance.unbalanced')}
          </span>
        )
      }
    >
      <ResponsiveTable
        rows={report.rows}
        columns={columns}
        rowKey={(r) => r.ledgerAccountId}
        // footLabel é o título do rodapé (tfoot no desktop, card de resumo no telefone) — não o header
        // emprestado de nenhuma coluna, então "Totais" nunca sai rotulado como "CÓDIGO: Totais" no
        // telefone (task 11, fix round 1). Por que footCell de débito/crédito usa report.total* em vez
        // de somar rows: ver comentário junto às colunas debit/credit acima.
        footLabel={<span style={{ fontWeight: 600 }}>{t('accounting.trialBalance.total')}</span>}
      />
    </Panel>
  );
}
