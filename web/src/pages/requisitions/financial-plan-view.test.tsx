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
              pixPayloadEmv: null,
            },
          ],
        })}
      />,
    );
    expect(screen.getByText(/Chave PIX: financeiro@empresa.com/)).toBeInTheDocument();
    expect(screen.getByText('chave alterada')).toBeInTheDocument();
  });

  it('mostra o PIX Copia e Cola da parcela PIX (tem precedência sobre a chave)', () => {
    const itau =
      '00020101021226770014BR.GOV.BCB.PIX2555api.itau/pix/qr/v2/2b4a01c0-cb2a-4d71-8338-d3a4d8f5c8af5204000053039865802BR5915TUNA PAGAMENTOS6013FLORIANOPOLIS62070503***63049F31';
    renderWithProviders(
      <FinancialPlanView
        req={req({
          installments: [
            {
              sequence: 1,
              dueDate: '2026-08-10',
              amount: 5000,
              paymentMethod: 'Pix',
              boletoDigitableLine: null,
              pixKeyOverride: null,
              pixPayloadEmv: itau,
            },
          ],
        })}
      />,
    );
    expect(screen.getByText(/PIX Copia e Cola:/)).toBeInTheDocument();
    expect(screen.getByText(/api\.itau\/pix\/qr\/v2\/2b4a01c0/)).toBeInTheDocument();
    expect(screen.getByText('copia e cola')).toBeInTheDocument();
  });
});
