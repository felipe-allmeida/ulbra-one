import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import './i18n';

const { mockUseMe } = vi.hoisted(() => ({ mockUseMe: vi.fn() }));
vi.mock('./queries', () => ({ useMe: mockUseMe }));

import { FinalApproverRedirect, RoleLanding } from './auth-guards';

function renderAt(path = '/app/home') {
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route
          path='/app/home'
          element={
            <FinalApproverRedirect>
              <div>HOME</div>
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
    expect(screen.getByText('HOME')).toBeInTheDocument();
  });

  it('Admin passa direto mesmo com todas as permissões (bypass não vira aprovador final)', () => {
    mockUseMe.mockReturnValue({ data: { isAdmin: true, permissions: [] } });
    renderAt();
    expect(screen.getByText('HOME')).toBeInTheDocument();
  });

  it('Controladoria passa direto (triagem não é aprovação final)', () => {
    mockUseMe.mockReturnValue({
      data: { isAdmin: false, permissions: ['requisitions:read', 'requisitions:triage'] },
    });
    renderAt();
    expect(screen.getByText('HOME')).toBeInTheDocument();
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
