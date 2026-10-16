import { describe, it, expect } from 'vitest';
import '../../i18n';
import i18next from 'i18next';
import type { TFunction } from 'i18next';
import { type Requisition } from '../../api';
import { paymentPlanBaseText } from './payment-plan';

const t = i18next.t.bind(i18next) as unknown as TFunction;
const req = (over: Record<string, unknown>): Requisition =>
  ({ installments: [], paymentFrequency: null, paymentInstallmentsCount: null, ...over }) as unknown as Requisition;

describe('paymentPlanBaseText', () => {
  it('usa as parcelas autoradas (front-load) como fonte da verdade — não "Parcela única"', () => {
    // 2 parcelas informadas direto (sem paymentInstallmentsCount) — o bug mostrava "Parcela única".
    expect(paymentPlanBaseText(req({ installments: [{}, {}] }), t)).toBe('2×');
  });

  it('1 parcela autorada → Parcela única', () => {
    expect(paymentPlanBaseText(req({ installments: [{}] }), t)).toBe('Parcela única');
  });

  it('sem plano autorado, cai no Nº de parcelas planejado', () => {
    expect(paymentPlanBaseText(req({ paymentInstallmentsCount: 3, paymentFrequency: 'Monthly' }), t)).toContain('3×');
  });

  it('sem nada → Parcela única', () => {
    expect(paymentPlanBaseText(req({}), t)).toBe('Parcela única');
  });
});
