import { describe, it, expect } from 'vitest';
import { domainForPath, itemForPath } from './nav.config';

describe('itemForPath', () => {
  it('matches an exact route', () => {
    expect(itemForPath('/app/contas-a-pagar')?.labelKey).toBe('nav.accountsPayable');
  });

  it('matches a detail route via the longest prefix', () => {
    expect(itemForPath('/app/contas-a-pagar/123/editar')?.labelKey).toBe('nav.accountsPayable');
  });

  it('disambiguates finance vs accounting "plano de contas"', () => {
    expect(itemForPath('/app/plano-de-contas')?.labelKey).toBe('nav.chartAccounts');
    expect(itemForPath('/app/contabil/plano-de-contas')?.labelKey).toBe('nav.ledgerAccounts');
  });

  it('returns null for an unknown route', () => {
    expect(itemForPath('/app/desconhecido')).toBeNull();
  });
});

describe('domainForPath', () => {
  it('resolves the owning domain', () => {
    expect(domainForPath('/app/contas-a-pagar').key).toBe('finance');
    expect(domainForPath('/app/contabil/dre').key).toBe('accounting');
    expect(domainForPath('/app/estrutura').key).toBe('structure');
    expect(domainForPath('/app/fornecedores').key).toBe('registry');
  });

  it('falls back to the first domain for unknown routes', () => {
    expect(domainForPath('/app/desconhecido').key).toBe('inicio');
  });
});
