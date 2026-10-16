import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import './i18n';

const { mockUseMe } = vi.hoisted(() => ({ mockUseMe: vi.fn() }));
vi.mock('./queries', () => ({ useMe: mockUseMe }));

import { FinalApproverRedirect, RequirePermission, RoleLanding } from './auth-guards';

function renderAt(path = '/app/structure') {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route
          path='/app/structure'
          element={
            <FinalApproverRedirect>
              <div>PASSTHROUGH</div>
            </FinalApproverRedirect>
          }
        />
        <Route path='/app/requisitions/approval' element={<div>INBOX</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

function renderLanding() {
  render(
    <MemoryRouter initialEntries={['/app']}>
      <Routes>
        <Route path='/app' element={<RoleLanding />} />
        <Route path='/app/requisitions/triage' element={<div>TRIAGE</div>} />
        <Route path='/app/requisitions/approval' element={<div>APPROVAL</div>} />
        <Route path='/app/pendencies' element={<div>PENDENCIES</div>} />
        <Route path='/app/treasury' element={<div>TREASURY</div>} />
        <Route path='/app/payables' element={<div>PAYABLES</div>} />
        <Route path='/app/vendors' element={<div>VENDORS</div>} />
        {/* Canary: the app has no /app/home anymore. If a landing regresses to it, the assertion
            fails showing HOME instead of a blank render. */}
        <Route path='/app/home' element={<div>HOME</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('FinalApproverRedirect', () => {
  beforeEach(() => vi.clearAllMocks());

  it('aprovador final é redirecionado para o Inbox', () => {
    mockUseMe.mockReturnValue({
      data: { isAdmin: false, permissions: ['requisitions:read', 'requisitions:approve', 'core:read'] },
    });
    renderAt();
    expect(screen.getByText('INBOX')).toBeInTheDocument();
  });

  it('Requester e Admin passam direto (passthrough)', () => {
    mockUseMe.mockReturnValue({
      data: { isAdmin: false, permissions: ['requisitions:read', 'requisitions:write'] },
    });
    renderAt();
    expect(screen.getByText('PASSTHROUGH')).toBeInTheDocument();
  });

  it('Admin passa direto mesmo com todas as permissões (bypass não vira aprovador final)', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: true, permissions: [] } });
    renderAt();
    expect(screen.getByText('PASSTHROUGH')).toBeInTheDocument();
  });

  it('Controladoria passa direto (triagem não é aprovação final)', () => {
    mockUseMe.mockReturnValue({
      data: { isAdmin: false, permissions: ['requisitions:read', 'requisitions:triage'] },
    });
    renderAt();
    expect(screen.getByText('PASSTHROUGH')).toBeInTheDocument();
  });
});

describe('RoleLanding', () => {
  beforeEach(() => vi.clearAllMocks());

  it('Controladoria pousa no Inbox de triagem — antes da aprovação financeira (payables/treasury)', () => {
    mockUseMe.mockReturnValue({
      data: {
        isAdmin: false,
        permissions: ['requisitions:read', 'requisitions:triage', 'payables:approve', 'treasury:approve'],
      },
    });
    renderLanding();
    expect(screen.getByText('TRIAGE')).toBeInTheDocument();
  });

  it('Aprovador pousa na fila de aprovação', () => {
    mockUseMe.mockReturnValue({
      data: { isAdmin: false, permissions: ['requisitions:read', 'requisitions:approve'] },
    });
    renderLanding();
    expect(screen.getByText('APPROVAL')).toBeInTheDocument();
  });

  it('Financeiro (pay + treasury:write, sem approve/triage) pousa na Gestão Financeira', () => {
    mockUseMe.mockReturnValue({
      data: {
        isAdmin: false,
        permissions: [
          'payables:read',
          'payables:write',
          'payables:pay',
          'receivables:read',
          'receivables:write',
          'treasury:read',
          'treasury:write',
          'requisitions:read',
          'requisitions:write',
        ],
      },
    });
    renderLanding();
    expect(screen.getByText('TREASURY')).toBeInTheDocument();
  });

  it('cargo com payables:write mas sem a assinatura do Financeiro segue para Contas a Pagar', () => {
    mockUseMe.mockReturnValue({
      data: { isAdmin: false, permissions: ['payables:read', 'payables:write'] },
    });
    renderLanding();
    expect(screen.getByText('PAYABLES')).toBeInTheDocument();
  });
});

describe('RoleLanding — sem o Início como para-choque', () => {
  beforeEach(() => vi.clearAllMocks());

  it('Admin pousa na primeira tela do rail (Gestão Financeira), não no Início', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: true, permissions: [] } });
    renderLanding();
    expect(screen.getByText('TREASURY')).toBeInTheDocument();
  });

  it('cargo sem assinatura de fluxo pousa na primeira tela que enxerga', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: false, permissions: ['registry:read'] } });
    renderLanding();
    expect(screen.getByText('VENDORS')).toBeInTheDocument();
  });
});

describe('RequirePermission — sem o Início como destino de negativa', () => {
  beforeEach(() => vi.clearAllMocks());

  function renderGuardAt(path: string) {
    render(
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route
            path={path}
            element={
              <RequirePermission anyOf={['payables:read']}>
                <div>SECRET</div>
              </RequirePermission>
            }
          />
          <Route path='/app/vendors' element={<div>VENDORS</div>} />
          {/* Same canary as renderLanding: /app/home must never be a redirect target again. */}
          <Route path='/app/home' element={<div>HOME</div>} />
        </Routes>
      </MemoryRouter>,
    );
  }

  it('negada: manda para a primeira tela visível do cargo', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: false, permissions: ['registry:read'] } });
    renderGuardAt('/app/payables');
    expect(screen.getByText('VENDORS')).toBeInTheDocument();
    expect(screen.queryByText('SECRET')).not.toBeInTheDocument();
  });

  it('negada já NA primeira tela visível: mostra o aviso em vez de redirecionar em loop', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: false, permissions: ['registry:read'] } });
    renderGuardAt('/app/vendors');
    expect(screen.getByText('Sem acesso a esta tela')).toBeInTheDocument();
    expect(screen.queryByText('SECRET')).not.toBeInTheDocument();
  });
});
