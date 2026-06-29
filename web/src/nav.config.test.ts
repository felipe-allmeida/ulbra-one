import { describe, it, expect } from 'vitest';
import { domainForPath, itemForPath } from './nav.config';

describe('itemForPath', () => {
  it('matches an exact route', () => {
    expect(itemForPath('/app/accounts-payable')?.labelKey).toBe('nav.accountsPayable');
  });

  it('matches a detail route via the longest prefix', () => {
    expect(itemForPath('/app/accounts-payable/123/edit')?.labelKey).toBe('nav.accountsPayable');
  });

  it('disambiguates finance vs accounting "chart of accounts"', () => {
    expect(itemForPath('/app/chart-of-accounts')?.labelKey).toBe('nav.chartAccounts');
    expect(itemForPath('/app/accounting/chart-of-accounts')?.labelKey).toBe('nav.ledgerAccounts');
  });

  it('returns null for an unknown route', () => {
    expect(itemForPath('/app/unknown')).toBeNull();
  });
});

describe('domainForPath', () => {
  it('resolves the owning domain', () => {
    expect(domainForPath('/app/accounts-payable').key).toBe('finance');
    expect(domainForPath('/app/accounting/income-statement').key).toBe('accounting');
    expect(domainForPath('/app/structure').key).toBe('structure');
    expect(domainForPath('/app/vendors').key).toBe('registry');
  });

  it('falls back to the first domain for unknown routes', () => {
    expect(domainForPath('/app/unknown').key).toBe('home');
  });
});
