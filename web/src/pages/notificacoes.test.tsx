import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import '../i18n';

const { mockList, mockUnread, mockMarkRead, mockMarkAll, markRead, markAll, navigate } = vi.hoisted(() => ({
  mockList: vi.fn(),
  mockUnread: vi.fn(),
  mockMarkRead: vi.fn(),
  mockMarkAll: vi.fn(),
  markRead: vi.fn(),
  markAll: vi.fn(),
  navigate: vi.fn(),
}));

vi.mock('../queries', () => ({
  useNotifications: mockList,
  useUnreadNotificationsCount: mockUnread,
  useMarkNotificationRead: mockMarkRead,
  useMarkAllNotificationsRead: mockMarkAll,
}));

vi.mock('../nav', () => ({ useOriginNav: () => navigate }));

import { NotificacoesPage } from './notificacoes';

/** 27/08/2026 15:30 local — every fixture below is written relative to this instant. */
const NOW = new Date(2026, 7, 27, 15, 30, 0);
const at = (day: number, hour = 12, minute = 0) => new Date(2026, 7, day, hour, minute).toISOString();

interface Row {
  id: string;
  type: string;
  title: string;
  message: string;
  referenceId: string | null;
  occurredAt: string;
  readAt: string | null;
}

const row = (over: Partial<Row> & Pick<Row, 'id' | 'type'>): Row => ({
  title: 'Requisição #4 aguardando aprovação',
  message: 'Uma requisição foi triada e aguarda aprovação.',
  referenceId: 'req_01',
  occurredAt: at(27, 15, 18),
  readAt: null,
  ...over,
});

function renderPage(items: Row[], unread = items.filter((i) => !i.readAt).length) {
  mockList.mockReturnValue({ data: { items, totalPages: 1 }, isLoading: false });
  mockUnread.mockReturnValue({ data: { count: unread } });
  return render(
    <MemoryRouter>
      <NotificacoesPage />
    </MemoryRouter>,
  );
}

/** The row of a given title. Clickable rows carry role="button", so they are not `listitem`s. */
function rowOf(title: string): HTMLElement {
  const row = screen.getByText(title).closest('.ntf-row');
  if (!row) throw new Error(`no .ntf-row for "${title}"`);
  return row as HTMLElement;
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(NOW);
  mockMarkRead.mockReturnValue({ mutate: markRead, isPending: false });
  mockMarkAll.mockReturnValue({ mutate: markAll, isPending: false });
});

afterEach(() => vi.useRealTimers());

describe('inbox de notificações', () => {
  it('mostra o tempo relativo, com a data absoluta no title', () => {
    renderPage([row({ id: 'n1', type: 'RequisitionAwaitingApproval', occurredAt: at(27, 13, 30) })]);
    expect(screen.getByText('há 2 h')).toBeTruthy();
    expect(screen.getByTitle('27/08/2026, 13:30')).toBeTruthy();
  });

  it('agrupa por dia, na ordem em que a lista chega', () => {
    renderPage([
      row({ id: 'n1', type: 'RequisitionAwaitingApproval', occurredAt: at(27, 15, 18) }),
      row({ id: 'n2', type: 'RequisitionApproved', occurredAt: at(26, 17, 2) }),
      row({ id: 'n3', type: 'RequisitionCancelled', occurredAt: at(24, 9, 5) }),
    ]);
    const groups = screen.getAllByRole('listitem').filter((li) => li.className.includes('ntf-day'));
    expect(groups.map((g) => g.textContent)).toEqual(['Hoje', 'Ontem', 'Esta semana']);
  });

  // A notificação de papel tem ReadAt compartilhado: quem marcar como lida limpa para o time inteiro.
  it('marca com o chip "Equipe" só o que é endereçado a um papel', () => {
    renderPage([
      row({ id: 'n1', type: 'RequisitionAwaitingApproval' }),
      row({ id: 'n2', type: 'RequisitionApproved', title: 'Requisição #7 aprovada para pagamento' }),
    ]);
    expect(within(rowOf('Requisição #4 aguardando aprovação')).queryByText('Equipe')).toBeTruthy();
    expect(within(rowOf('Requisição #7 aprovada para pagamento')).queryByText('Equipe')).toBeNull();
  });

  it('dá tom de urgência ao SLA estourado e tom neutro à espera de rotina', () => {
    renderPage([
      row({ id: 'n1', type: 'RequisitionTriageSlaOverdue', title: 'SLA de triagem estourado — Requisição #4' }),
      row({ id: 'n2', type: 'RequisitionAwaitingTriage' }),
    ]);
    expect(rowOf('SLA de triagem estourado — Requisição #4').className).toContain('ntf-danger');
    expect(rowOf('Requisição #4 aguardando aprovação').className).toContain('ntf-info');
  });

  it('marca como lida sem tirar o leitor da caixa de entrada', async () => {
    const user = userEvent.setup();
    renderPage([row({ id: 'n1', type: 'RequisitionAwaitingApproval' })]);
    await user.click(screen.getByRole('button', { name: 'Marcar como lida' }));
    expect(markRead).toHaveBeenCalledWith('n1');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('clicar na linha abre a entidade de origem e marca como lida', async () => {
    const user = userEvent.setup();
    renderPage([row({ id: 'n1', type: 'RequisitionAwaitingApproval', referenceId: 'req_42' })]);
    await user.click(screen.getByText('Requisição #4 aguardando aprovação'));
    expect(navigate).toHaveBeenCalledWith('/app/requisitions/req_42');
    expect(markRead).toHaveBeenCalledWith('n1');
  });

  it('não oferece marcar como lida no que já foi lido', () => {
    renderPage([row({ id: 'n1', type: 'RequisitionAwaitingApproval', readAt: at(27, 15, 20) })]);
    expect(screen.queryByRole('button', { name: 'Marcar como lida' })).toBeNull();
  });

  it('desabilita "marcar todas" quando não há o que marcar', () => {
    renderPage([row({ id: 'n1', type: 'RequisitionAwaitingApproval', readAt: at(27, 15, 20) })], 0);
    expect(screen.getByRole('button', { name: 'Marcar todas como lidas' }).hasAttribute('disabled')).toBe(true);
  });

  it('explica o vazio de acordo com o filtro ativo', async () => {
    const user = userEvent.setup();
    renderPage([]);
    expect(screen.getByText('Nenhuma notificação')).toBeTruthy();
    await user.click(screen.getByLabelText('Somente não lidas'));
    expect(screen.getByText('Nenhuma notificação não lida')).toBeTruthy();
  });

  it('renderiza um tipo que o frontend ainda não conhece em vez de deixar a linha quebrada', () => {
    renderPage([row({ id: 'n1', type: 'SomethingTheBackendAddedLater', title: 'Aviso novo' })]);
    expect(rowOf('Aviso novo').className).toContain('ntf-info');
  });
});
