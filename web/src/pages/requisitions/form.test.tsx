import { describe, it, expect } from 'vitest';
import { installmentsToRows } from './form';
import { type Requisition } from '../../api';

// Regression: editing must load each installment's PIX key override. Otherwise the field
// opened empty and save wiped the stored key (reported bug — vendor PIX installment lost its key).
describe('installmentsToRows — pré-preenche as parcelas ao editar', () => {
  it('carrega o override de chave PIX de uma parcela PIX', () => {
    const installments = [
      {
        sequence: 1,
        dueDate: '2026-08-31',
        amount: 4000,
        paymentMethod: 'Pix',
        boletoDigitableLine: null,
        pixKeyOverride: 'financeiro@empresa.com',
      },
    ] as unknown as Requisition['installments'];

    const rows = installmentsToRows(installments);

    expect(rows[0].pixKeyOverride).toBe('financeiro@empresa.com'); // value is not lost on edit
    expect(rows[0].paymentMethod).toBe('Pix');
    expect(rows[0].amount).toBe('4000');
  });

  it('mantém boleto e trata override ausente como vazio', () => {
    const installments = [
      {
        sequence: 1,
        dueDate: '2026-09-10',
        amount: 500,
        paymentMethod: 'Boleto',
        boletoDigitableLine: '34191790010104351004791020150008291070026000',
        pixKeyOverride: null,
      },
    ] as unknown as Requisition['installments'];

    const rows = installmentsToRows(installments);

    expect(rows[0].pixKeyOverride).toBe('');
    expect(rows[0].boletoDigitableLine).toBe('34191790010104351004791020150008291070026000');
  });
});
