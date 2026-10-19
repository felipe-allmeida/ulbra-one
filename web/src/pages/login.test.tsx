import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { screen } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { renderWithProviders } from '../test/renderWithProviders';
import '../i18n';
import { LoginPage } from './login';

/**
 * The login is the app's front door: there is no landing page, so `/` IS the login.
 * These tests pin that contract — the root renders the form, and a session that is
 * still valid skips the form entirely instead of asking the user to sign in again.
 */

/** A JWT whose only claim that matters here is `exp` (seconds since epoch). */
function tokenExpiringIn(seconds: number): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + seconds }));
  return `header.${payload}.signature`;
}

function renderRoot() {
  renderWithProviders(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route path='/' element={<LoginPage />} />
        <Route path='/app' element={<div>APP</div>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('LoginPage na raiz', () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.restoreAllMocks());

  it('renderiza o login em `/` (sem landing no caminho)', () => {
    renderRoot();
    expect(screen.getByRole('heading', { name: 'Entrar no ULBRA One' })).toBeInTheDocument();
  });

  it('não oferece mais "voltar para o site" — a raiz já é o login', () => {
    renderRoot();
    expect(screen.queryByRole('link', { name: /voltar para o site/i })).not.toBeInTheDocument();
  });

  it('mostra o painel de marca do time de Tecnologia', () => {
    renderRoot();
    expect(screen.getByText('Tecnologia & Inovação')).toBeInTheDocument();
    expect(screen.getByText(/construído internamente/i)).toBeInTheDocument();
  });

  it('com sessão válida vai direto para /app em vez de pedir login de novo', () => {
    localStorage.setItem('ulbra-one.token', tokenExpiringIn(3600));
    renderRoot();
    expect(screen.getByText('APP')).toBeInTheDocument();
  });

  it('token expirado não pula o login', () => {
    localStorage.setItem('ulbra-one.token', tokenExpiringIn(-3600));
    renderRoot();
    expect(screen.getByRole('heading', { name: 'Entrar no ULBRA One' })).toBeInTheDocument();
  });
});
