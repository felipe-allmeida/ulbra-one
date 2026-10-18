import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';
import { AuditTimeline, type AuditEvent } from './AuditTimeline';

const events: AuditEvent[] = [
  { type: 'Created', actorName: 'João Silva', timestamp: '2026-06-20T10:30:00Z' },
  { type: 'Approved', actorName: 'Carlos Lima', timestamp: '2026-06-21T14:15:00Z' },
];

describe('AuditTimeline', () => {
  it('renders each event with its translated description and actor', () => {
    render(<AuditTimeline events={events} />);
    expect(screen.getByText('Título criado')).toBeInTheDocument();
    expect(screen.getByText('Aprovado')).toBeInTheDocument();
    expect(screen.getByText('por João Silva')).toBeInTheDocument();
  });
  it('renders an empty message when there are no events', () => {
    render(<AuditTimeline events={[]} />);
    expect(screen.getByText('Nenhum evento registrado')).toBeInTheDocument();
  });
  it('falls back to the raw type for unknown event types', () => {
    render(<AuditTimeline events={[{ type: 'SomethingNew', actorName: 'Sys', timestamp: '2026-06-20T10:30:00Z' }]} />);
    expect(screen.getByText('SomethingNew')).toBeInTheDocument();
  });
  it('shows the actor photo when the event carries one', () => {
    render(
      <AuditTimeline
        events={[
          {
            type: 'Approved',
            actorName: 'Carlos Lima',
            actorPictureUrl: 'https://lh3.googleusercontent.com/a/carlos',
            timestamp: '2026-06-21T14:15:00Z',
          },
        ]}
      />,
    );
    expect(screen.getByRole('img', { name: 'Carlos Lima' })).toHaveAttribute(
      'src',
      'https://lh3.googleusercontent.com/a/carlos',
    );
  });
  it('falls back to the actor initials when there is no photo', () => {
    render(<AuditTimeline events={events} />);
    expect(screen.getByText('JS')).toBeInTheDocument();
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
  });
});
