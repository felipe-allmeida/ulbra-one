import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../i18n';
import { PresenceAvatars } from './PresenceAvatars';

const presence = vi.hoisted(() => ({ users: [] as { userId: string; name: string; pictureUrl?: string | null }[] }));
const me = vi.hoisted(() => ({ data: { userId: 'user_00000000000000000000000000000001', name: 'Felipe Almeida' } }));

vi.mock('./usePresence', () => ({ usePresence: () => presence }));
vi.mock('../queries', () => ({ useMe: () => me }));

describe('PresenceAvatars', () => {
  beforeEach(() => {
    presence.users = [];
  });

  it('mostra você mesmo, rotulado, quando está sozinho no recurso', () => {
    presence.users = [{ userId: '00000000-0000-0000-0000-000000000001', name: 'Felipe Almeida' }];

    render(<PresenceAvatars scope='requisition' resourceId='r1' />);

    expect(screen.getByTitle('Você')).toBeInTheDocument();
  });

  // Sem isso os dois discos ficam anônimos e não dá para saber qual é você.
  it('põe você na frente dos demais', () => {
    presence.users = [
      { userId: '00000000-0000-0000-0000-000000000002', name: 'Maico Souza' },
      { userId: '00000000-0000-0000-0000-000000000001', name: 'Felipe Almeida' },
    ];

    render(<PresenceAvatars scope='requisition' resourceId='r1' />);

    const titles = screen.getAllByTitle(/./).map((e) => e.getAttribute('title'));
    expect(titles).toEqual(['Você', 'Maico Souza']);
  });

  // O backplane fora devolve lista vazia; presença é acessório e some sem quebrar a tela.
  it('não renderiza nada quando a presença não respondeu', () => {
    const { container } = render(<PresenceAvatars scope='requisition' resourceId='r1' />);

    expect(container).toBeEmptyDOMElement();
  });
});
