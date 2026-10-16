import { describe, it, expect } from 'vitest';
import { NAV_DOMAINS, domainForPath, domainVisibleFor, itemForPath, itemVisibleFor } from './nav.config';
import type { Me } from './permissions';

describe('itemForPath', () => {
  it('matches an exact route', () => {
    expect(itemForPath('/app/payables')?.labelKey).toBe('nav.accountsPayable');
  });

  it('matches a detail route via the longest prefix', () => {
    expect(itemForPath('/app/payables/123/edit')?.labelKey).toBe('nav.accountsPayable');
  });

  it('resolves the single global chart of accounts under accounting (AEL-684)', () => {
    expect(itemForPath('/app/accounting/chart-of-accounts')?.labelKey).toBe('nav.ledgerAccounts');
    // The old Finance chart-of-accounts route was removed when ChartAccount was consolidated.
    expect(itemForPath('/app/chart-of-accounts')).toBeNull();
  });

  it('resolves the Livro Diário under the accounting books subgroup', () => {
    expect(itemForPath('/app/accounting/journal-book')?.labelKey).toBe('nav.journalBook');
  });

  it('resolves the audit trail under settings', () => {
    expect(itemForPath('/app/settings/audit')?.labelKey).toBe('nav.audit');
    expect(itemForPath('/app/settings/audit')?.requires).toEqual(['audit:read']);
  });

  it('returns null for an unknown route', () => {
    expect(itemForPath('/app/unknown')).toBeNull();
  });

  it('returns null for the home route (the domain is routable, with no sub-item)', () => {
    expect(itemForPath('/app/home')).toBeNull();
  });
});

describe('domainForPath', () => {
  it('resolves the owning domain', () => {
    expect(domainForPath('/app/payables').key).toBe('finance');
    expect(domainForPath('/app/accounting/income-statement').key).toBe('accounting');
    expect(domainForPath('/app/structure').key).toBe('structure');
    expect(domainForPath('/app/vendors').key).toBe('registry');
  });

  it('resolves Centro de Custo under structure and Revisão de Contabilidade under accounting', () => {
    // Cost centers moved to the Estrutura domain with a top-level route (AEL-891).
    expect(domainForPath('/app/cost-centers').key).toBe('structure');
    expect(domainForPath('/app/cost-centers/123').key).toBe('structure');
    // The unified Contábil home (redesign) lives in the accounting domain.
    expect(domainForPath('/app/accounting/review').key).toBe('accounting');
  });

  it('resolves the home domain via its domain-level route', () => {
    expect(domainForPath('/app/home').key).toBe('home');
  });

  it('falls back to the first domain for unknown routes', () => {
    expect(domainForPath('/app/unknown').key).toBe('home');
  });
});

describe('visibilidade por papel (AEL-859)', () => {
  const find = (key: string) => NAV_DOMAINS.find((d) => d.key === key)!;
  const budgetItems = () => find('budget').groups.flatMap((g) => g.items);
  const item = (labelKey: string) => budgetItems().find((i) => i.labelKey === labelKey)!;

  const sup = {
    isAdmin: false,
    permissions: ['requisitions:read', 'requisitions:approve', 'core:read', 'registry:read', 'projects:read'],
  } as unknown as Me;
  const requester = {
    isAdmin: false,
    permissions: ['requisitions:read', 'requisitions:write', 'core:read', 'registry:read', 'projects:read'],
  } as unknown as Me;
  const admin = { isAdmin: true, permissions: [] } as unknown as Me;

  it('aprovador final: só o domínio Orçamento; Início/Cadastros/Estrutura/Config somem', () => {
    expect(domainVisibleFor(sup, find('budget'))).toBe(true);
    expect(domainVisibleFor(sup, find('home'))).toBe(false);
    expect(domainVisibleFor(sup, find('registry'))).toBe(false);
    expect(domainVisibleFor(sup, find('structure'))).toBe(false);
    expect(domainVisibleFor(sup, find('settings'))).toBe(false);
  });

  it('aprovador final: itens Dashboard/Aprovação/Orçamentos visíveis; a lista completa some', () => {
    expect(itemVisibleFor(sup, item('nav.requisitionOverview'))).toBe(true);
    expect(itemVisibleFor(sup, item('nav.requisitionApproval'))).toBe(true);
    expect(itemVisibleFor(sup, item('nav.budgets'))).toBe(true);
    expect(itemVisibleFor(sup, item('nav.requisitions'))).toBe(false);
  });

  it('regressão: Requester e Admin mantêm a visibilidade atual', () => {
    expect(domainVisibleFor(requester, find('home'))).toBe(true);
    expect(domainVisibleFor(requester, find('registry'))).toBe(true);
    expect(itemVisibleFor(requester, item('nav.requisitions'))).toBe(true);
    expect(itemVisibleFor(requester, item('nav.requisitionOverview'))).toBe(false);
    expect(domainVisibleFor(admin, find('home'))).toBe(true);
    expect(itemVisibleFor(admin, item('nav.requisitions'))).toBe(true);
    expect(itemVisibleFor(admin, item('nav.requisitionOverview'))).toBe(true);
  });
});

describe('Controladoria (AEL-860)', () => {
  const find = (key: string) => NAV_DOMAINS.find((d) => d.key === key)!;
  const budgetItem = (labelKey: string) =>
    find('budget')
      .groups.flatMap((g) => g.items)
      .find((i) => i.labelKey === labelKey)!;
  const accItem = (labelKey: string) =>
    find('accounting')
      .groups.flatMap((g) => g.items)
      .find((i) => i.labelKey === labelKey)!;
  const financeItem = (labelKey: string) =>
    find('finance')
      .groups.flatMap((g) => g.items)
      .find((i) => i.labelKey === labelKey)!;

  // Faithful subset of the Controllership RoleSeed: triages requisitions + reads Accounting + treasury
  // read/approve (NO treasury:write), payables/receivables:read. No accounting or treasury write.
  const controller = {
    isAdmin: false,
    permissions: [
      'requisitions:read',
      'requisitions:triage',
      'payables:read',
      'payables:approve',
      'receivables:read',
      'treasury:read',
      'treasury:approve',
      'accounting:read',
      'reports:read',
      'fiscal:read',
      'core:read',
      'budget-adjustments:read',
      'budget-adjustments:write',
      'registry:read',
      'registry:write',
    ],
  } as unknown as Me;
  const finance = {
    isAdmin: false,
    permissions: ['payables:read', 'payables:write', 'treasury:read', 'treasury:write', 'receivables:read'],
  } as unknown as Me;

  it('vê o domínio Orçamento e compartilha Dashboard/Orçamentos (gate approve|triage, nunca read)', () => {
    expect(domainVisibleFor(controller, find('budget'))).toBe(true);
    expect(itemVisibleFor(controller, budgetItem('nav.requisitionTriage'))).toBe(true);
    expect(itemVisibleFor(controller, budgetItem('nav.requisitionOverview'))).toBe(true);
    expect(itemVisibleFor(controller, budgetItem('nav.budgets'))).toBe(true);
  });

  it('vê as telas de Contábil em leitura (§6e), incluindo a home de Revisão (ações bloqueadas na tela)', () => {
    expect(domainVisibleFor(controller, find('accounting'))).toBe(true);
    expect(itemVisibleFor(controller, accItem('nav.journalBook'))).toBe(true);
    expect(itemVisibleFor(controller, accItem('nav.generalLedger'))).toBe(true);
    expect(itemVisibleFor(controller, accItem('nav.trialBalance'))).toBe(true);
    expect(itemVisibleFor(controller, accItem('nav.incomeStatement'))).toBe(true);
    // The unified "Revisão de Contabilidade" home (accounting:read) is visible read-only (AEL-865):
    // Registro de Despesa/Classificação were absorbed into it; classify actions are now gated INSIDE
    // the screen (payables:write, which Controllership lacks), not in the menu.
    expect(itemVisibleFor(controller, accItem('nav.accountingReview'))).toBe(true);
  });

  it('não vê a home Gestão Financeira nem Conciliação (operações de tesouraria = pay/treasury:write)', () => {
    // Sees the Finance domain (payables/receivables:read + pendencies), but not the treasury home
    // (absorbed Remessas as a tab) nor Conciliação (standalone screen, treasury:write).
    expect(domainVisibleFor(controller, find('finance'))).toBe(true);
    expect(itemVisibleFor(controller, financeItem('nav.treasuryHome'))).toBe(false);
    expect(itemVisibleFor(controller, financeItem('nav.conciliacao'))).toBe(false);
    // Finance (treasury:write) sees both the home and the standalone Conciliação.
    expect(itemVisibleFor(finance, financeItem('nav.treasuryHome'))).toBe(true);
    expect(itemVisibleFor(finance, financeItem('nav.conciliacao'))).toBe(true);
  });
});
