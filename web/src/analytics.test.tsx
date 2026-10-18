import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

const getEnvironment = vi.fn(() => 'production');
vi.mock('./config/env', () => ({ getEnvironment: () => getEnvironment() }));
vi.mock('react-router-dom', () => ({
  useLocation: () => ({ pathname: '/app/payables/payable_495ba26af7804d4b9dc9ff2cc06ed411' }),
}));
vi.mock('./permissions', () => ({ usePermissions: () => ({ me: { role: 'Controladoria' } }) }));
vi.mock('./store', () => ({ useApp: () => ({ empresaId: 'company_1' }) }));

import { clarityRoute, clarityTag, initClarity, maskRoute } from './analytics';

/**
 * Sem máscara, cada `payable_…`/`req_…` vira uma "página" distinta no dashboard do Clarity e
 * estilhaça toda a agregação — foi por isso que a primeira análise precisou normalizar as URLs
 * na mão. Mascarar também evita mandar id de registro pra um terceiro.
 */
describe('maskRoute', () => {
  it('troca um id prefixado por :id', () => {
    expect(maskRoute('/app/payables/payable_495ba26af7804d4b9dc9ff2cc06ed411')).toBe('/app/payables/:id');
  });

  it('troca um UUID por :id', () => {
    expect(maskRoute('/app/payables/3fa85f64-5717-4219-9730-000000000000')).toBe('/app/payables/:id');
  });

  it('mascara id no meio do caminho', () => {
    expect(maskRoute('/app/requisitions/approval/req_8c493e682f7a4d539436545148a37382')).toBe(
      '/app/requisitions/approval/:id',
    );
  });

  it('mascara mais de um id no mesmo caminho', () => {
    expect(
      maskRoute(
        '/app/payables/payable_495ba26af7804d4b9dc9ff2cc06ed411/parcelas/inst_1234567890abcdef1234567890abcdef',
      ),
    ).toBe('/app/payables/:id/parcelas/:id');
  });

  it('não mexe numa rota sem id', () => {
    expect(maskRoute('/app/treasury')).toBe('/app/treasury');
  });

  it('não confunde segmento de texto com id', () => {
    expect(maskRoute('/app/requisitions/triage')).toBe('/app/requisitions/triage');
  });
});

describe('initClarity', () => {
  beforeEach(() => {
    document.head.innerHTML = '';
    delete window.clarity;
    getEnvironment.mockReturnValue('production');
  });
  afterEach(() => {
    document.head.innerHTML = '';
    delete window.clarity;
  });

  const tag = () => document.querySelector('script[src*="clarity.ms"]');

  it('injeta a tag em produção', () => {
    initClarity();

    expect(tag()).not.toBeNull();
  });

  /**
   * ~2/3 das sessões coletadas vinham de localhost e dev.one.ulbra.ai, diluindo toda métrica.
   * O Terraform injeta VITE_ENVIRONMENT por ambiente, então o gate resolve os dois.
   */
  it('não injeta a tag fora de produção', () => {
    getEnvironment.mockReturnValue('development');

    initClarity();

    expect(tag()).toBeNull();
  });

  it('não injeta a tag em staging', () => {
    getEnvironment.mockReturnValue('staging');

    initClarity();

    expect(tag()).toBeNull();
  });

  it('não injeta a tag duas vezes', () => {
    initClarity();
    initClarity();

    expect(document.querySelectorAll('script[src*="clarity.ms"]')).toHaveLength(1);
  });
});

describe('clarityTag / clarityRoute', () => {
  beforeEach(() => {
    delete window.clarity;
    getEnvironment.mockReturnValue('production');
  });

  it('encaminha uma tag customizada pro clarity', () => {
    const spy = vi.fn();
    window.clarity = spy;

    clarityTag('role', 'Controladoria');

    expect(spy).toHaveBeenCalledWith('set', 'role', 'Controladoria');
  });

  it('manda a rota já mascarada', () => {
    const spy = vi.fn();
    window.clarity = spy;

    clarityRoute('/app/payables/payable_495ba26af7804d4b9dc9ff2cc06ed411');

    expect(spy).toHaveBeenCalledWith('set', 'route', '/app/payables/:id');
  });

  it('não quebra quando a tag não carregou', () => {
    expect(() => clarityTag('role', 'Controladoria')).not.toThrow();
    expect(() => clarityRoute('/app/treasury')).not.toThrow();
  });

  it('ignora tag com valor vazio', () => {
    const spy = vi.fn();
    window.clarity = spy;

    clarityTag('company', '');

    expect(spy).not.toHaveBeenCalled();
  });
});

describe('useClaritySession', () => {
  beforeEach(() => {
    delete window.clarity;
    getEnvironment.mockReturnValue('production');
  });

  it('reporta a rota mascarada e o cargo, sem identificar o usuário', async () => {
    const spy = vi.fn();
    window.clarity = spy;
    const { renderHook } = await import('@testing-library/react');
    const { useClaritySession } = await import('./analytics');

    renderHook(() => useClaritySession());

    expect(spy).toHaveBeenCalledWith('set', 'route', '/app/payables/:id');
    expect(spy).toHaveBeenCalledWith('set', 'role', 'Controladoria');
    expect(spy).toHaveBeenCalledWith('set', 'company', 'company_1');
    // identify mandaria um id estável de funcionário pra um terceiro — fora de escopo por decisão.
    expect(spy.mock.calls.some((c) => c[0] === 'identify')).toBe(false);
  });
});
