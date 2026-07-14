import { describe, it, expect } from 'vitest';
import { domainForPath, itemForPath } from './nav.config';

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

  it('resolves Centro de Custo and Classificação under the accounting domain (AEL-678 menu move)', () => {
    expect(domainForPath('/app/accounting/cost-centers').key).toBe('accounting');
    expect(domainForPath('/app/accounting/cost-centers/123').key).toBe('accounting');
    expect(domainForPath('/app/accounting/classification').key).toBe('accounting');
  });

  it('resolves the home domain via its domain-level route', () => {
    expect(domainForPath('/app/home').key).toBe('home');
  });

  it('falls back to the first domain for unknown routes', () => {
    expect(domainForPath('/app/unknown').key).toBe('home');
  });
});
