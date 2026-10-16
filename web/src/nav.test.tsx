import { useState } from 'react';
import { describe, it, expect } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import './i18n';
import { BackLink, useBackNavigation, useOriginNav, usePageParam, useResetPageOnChange } from './nav';

/* ── useOriginNav ─────────────────────────────────────────────────────────── */

function Emitter() {
  const open = useOriginNav();
  return (
    <button type='button' onClick={() => open('/dest')}>
      go
    </button>
  );
}
function OriginProbe() {
  const from = (useLocation().state as { from?: string } | null)?.from ?? 'none';
  return <div data-testid='from'>{from}</div>;
}

describe('useOriginNav', () => {
  it('stamps the current pathname + search as location.state.from', () => {
    render(
      <MemoryRouter initialEntries={['/list?stage=approval&page=2']}>
        <Routes>
          <Route path='/list' element={<Emitter />} />
          <Route path='/dest' element={<OriginProbe />} />
        </Routes>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('go'));
    expect(screen.getByTestId('from')).toHaveTextContent('/list?stage=approval&page=2');
  });
});

/* ── useBackNavigation ────────────────────────────────────────────────────── */

function BackProbe() {
  const { to } = useBackNavigation('/fallback');
  return <div data-testid='to'>{to}</div>;
}

describe('useBackNavigation', () => {
  it('resolves the captured origin', () => {
    render(
      <MemoryRouter initialEntries={[{ pathname: '/detail', state: { from: '/list?stage=x' } }]}>
        <BackProbe />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('to')).toHaveTextContent('/list?stage=x');
  });

  it('falls back to the parent list when there is no origin (fresh deep-link)', () => {
    render(
      <MemoryRouter initialEntries={['/detail']}>
        <BackProbe />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('to')).toHaveTextContent('/fallback');
  });

  it('falls back when history state is malformed', () => {
    render(
      <MemoryRouter initialEntries={[{ pathname: '/detail', state: { unrelated: 1 } }]}>
        <BackProbe />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('to')).toHaveTextContent('/fallback');
  });
});

/* ── BackLink ─────────────────────────────────────────────────────────────── */

describe('BackLink', () => {
  it('renders the generic "Voltar" label and navigates to the origin on click', () => {
    render(
      <MemoryRouter initialEntries={[{ pathname: '/detail', state: { from: '/list?stage=x' } }]}>
        <Routes>
          <Route path='/detail' element={<BackLink fallback='/fallback' />} />
          <Route path='/list' element={<div>ORIGIN LIST</div>} />
          <Route path='/fallback' element={<div>FALLBACK LIST</div>} />
        </Routes>
      </MemoryRouter>,
    );
    expect(screen.getByText('Voltar')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Voltar'));
    expect(screen.getByText('ORIGIN LIST')).toBeInTheDocument();
  });

  it('navigates to the fallback when there is no origin', () => {
    render(
      <MemoryRouter initialEntries={['/detail']}>
        <Routes>
          <Route path='/detail' element={<BackLink fallback='/fallback' />} />
          <Route path='/fallback' element={<div>FALLBACK LIST</div>} />
        </Routes>
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByText('Voltar'));
    expect(screen.getByText('FALLBACK LIST')).toBeInTheDocument();
  });

  it('names the origin screen when it maps to a nav item ("Voltar para …")', () => {
    // Origin is a real nav route (/app/payables) → the label names the screen.
    render(
      <MemoryRouter initialEntries={[{ pathname: '/app/payables/p1', state: { from: '/app/payables?stage=payment' } }]}>
        <BackLink fallback='/app/payables' />
      </MemoryRouter>,
    );
    expect(screen.getByRole('button')).toHaveTextContent(/^Voltar para .+/);
  });

  it('stays generic "Voltar" when the target is not a side-nav screen', () => {
    // /app/entries is not in the nav config → no screen name → generic label.
    render(
      <MemoryRouter initialEntries={['/app/entries/e1']}>
        <BackLink fallback='/app/entries' />
      </MemoryRouter>,
    );
    expect(screen.getByRole('button')).toHaveTextContent('Voltar');
    expect(screen.getByRole('button')).not.toHaveTextContent('Voltar para');
  });
});

/* ── usePageParam + useResetPageOnChange ──────────────────────────────────── */

function PageProbe({ dep }: { dep: string }) {
  const [page, setPage] = usePageParam();
  useResetPageOnChange(setPage, [dep]);
  return (
    <div>
      <div data-testid='page'>{page}</div>
      <div data-testid='search'>{useLocation().search}</div>
      <button type='button' onClick={() => setPage(3)}>
        set3
      </button>
    </div>
  );
}
function ResetHarness() {
  const [dep, setDep] = useState('a');
  return (
    <>
      <PageProbe dep={dep} />
      <button type='button' onClick={() => setDep('b')}>
        changeDep
      </button>
    </>
  );
}

describe('usePageParam', () => {
  it('reads the page from the URL, defaulting to 1', () => {
    render(
      <MemoryRouter initialEntries={['/list?page=4']}>
        <PageProbe dep='a' />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('page')).toHaveTextContent('4');
  });

  it('writes ?page= to the URL (and drops it for page 1)', () => {
    render(
      <MemoryRouter initialEntries={['/list']}>
        <PageProbe dep='a' />
      </MemoryRouter>,
    );
    expect(screen.getByTestId('page')).toHaveTextContent('1');
    fireEvent.click(screen.getByText('set3'));
    expect(screen.getByTestId('page')).toHaveTextContent('3');
    expect(screen.getByTestId('search')).toHaveTextContent('page=3');
  });
});

describe('useResetPageOnChange', () => {
  it('keeps a restored ?page= on mount but resets to 1 when the dependency changes', () => {
    render(
      <MemoryRouter initialEntries={['/list?page=3']}>
        <ResetHarness />
      </MemoryRouter>,
    );
    // Mount must NOT reset — this is what makes "back" restore the exact page.
    expect(screen.getByTestId('page')).toHaveTextContent('3');
    // A real filter/scope change resets to page 1.
    fireEvent.click(screen.getByText('changeDep'));
    expect(screen.getByTestId('page')).toHaveTextContent('1');
  });
});
