import { describe, it, expect } from 'vitest';
import { screen } from '@testing-library/react';
import '../../i18n';
import { renderWithProviders } from '../../test/renderWithProviders';
import { type Requisition } from '../../api';
import { FinancialPlanView } from './financial-plan-view';

const req = (over: Partial<Requisition> = {}): Requisition =>
  ({
    id: 'req_1',
    issueDate: '2026-08-01',
    installments: [
      { sequence: 1, dueDate: '2026-08-10', amount: 2000, paymentMethod: 'Pix', boletoDigitableLine: null },
      {
        sequence: 2,
        dueDate: '2026-09-10',
        amount: 3000,
        paymentMethod: 'Boleto',
        boletoDigitableLine: '34191790010104351004791020150008291070026000',
      },
    ],
    ...over,
  }) as unknown as Requisition;

describe('FinancialPlanView', () => {
  it('não renderiza nada quando não há plano front-loaded', () => {
    renderWithProviders(<FinancialPlanView req={req({ installments: [] })} />);
    expect(screen.queryByText('Informações financeiras')).not.toBeInTheDocument();
  });

  it('mostra emissão, total e as parcelas em somente-leitura', () => {
    renderWithProviders(<FinancialPlanView req={req()} />);
    expect(screen.getByText('Informações financeiras')).toBeInTheDocument();
    expect(screen.getByText('01/08/2026')).toBeInTheDocument(); // emissão
    expect(screen.getByText('R$ 5.000,00')).toBeInTheDocument(); // total programado (2000 + 3000)
    expect(screen.getByText('10/08/2026')).toBeInTheDocument(); // 1ª parcela
    // Linha digitável do boleto + selo de convênio (linha começa com 8? não — bancário, sem selo).
    expect(screen.getByText(/34191790010104351004791020150008291070026000/)).toBeInTheDocument();
  });

  it('mostra a chave PIX alterada (override) da parcela PIX', () => {
    renderWithProviders(
      <FinancialPlanView
        req={req({
          installments: [
            {
              sequence: 1,
              dueDate: '2026-08-10',
              amount: 2000,
              paymentMethod: 'Pix',
              boletoDigitableLine: null,
              pixKeyOverride: 'financeiro@empresa.com',
            },
          ],
        })}
      />,
    );
    expect(screen.getByText(/Chave PIX: financeiro@empresa.com/)).toBeInTheDocument();
    expect(screen.getByText('chave alterada')).toBeInTheDocument();
  });
});
