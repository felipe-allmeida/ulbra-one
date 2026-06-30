import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../test/renderWithProviders';
import { GoogleSignInButton } from './google-signin';
import { getGoogleClientId } from '../config/env';

// Keep the rest of config/env real (api.ts reads getApiBaseUrl); only stub the client-id getter.
vi.mock('../config/env', async (orig) => ({
  ...(await orig<typeof import('../config/env')>()),
  getGoogleClientId: vi.fn(),
}));

const mockClientId = getGoogleClientId as ReturnType<typeof vi.fn>;

interface InitConfig {
  client_id: string;
  callback: (r: { credential?: string }) => void;
}

describe('GoogleSignInButton', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    delete (window as { google?: unknown }).google;
  });

  it('renders the disabled "em breve" fallback when no client id is configured', () => {
    mockClientId.mockReturnValue(undefined);
    renderWithProviders(<GoogleSignInButton onCredential={() => {}} />);
    const btn = screen.getByRole('button', { name: /Google/i });
    expect(btn).toBeDisabled();
  });

  it('initializes GIS and forwards the credential when a client id is configured', async () => {
    mockClientId.mockReturnValue('client-abc-123');

    let captured: InitConfig | null = null;
    const initialize = vi.fn((cfg: InitConfig) => {
      captured = cfg;
    });
    const renderButton = vi.fn();
    // GIS already present → loadGis resolves immediately (no script injection in jsdom).
    (window as { google?: unknown }).google = { accounts: { id: { initialize, renderButton } } };

    const onCredential = vi.fn();
    renderWithProviders(<GoogleSignInButton onCredential={onCredential} />);

    await waitFor(() => expect(initialize).toHaveBeenCalledTimes(1));
    expect(renderButton).toHaveBeenCalledTimes(1);
    expect(captured!.client_id).toBe('client-abc-123');

    // Simulate Google returning an ID token to the registered callback.
    captured!.callback({ credential: 'google-id-token-xyz' });
    expect(onCredential).toHaveBeenCalledWith('google-id-token-xyz');
  });
});
