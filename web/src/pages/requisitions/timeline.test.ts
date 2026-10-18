import { describe, it, expect } from 'vitest';
import { activeReason, transitionsToEvents } from './timeline';
import { type Requisition, type RequisitionTransition } from '../../api';

// activeReason só lê a ação + motivo da ÚLTIMA transição; os demais campos são só para satisfazer o tipo.
const tr = (action: string, reason: string | null): RequisitionTransition =>
  ({
    action,
    fromStatus: 'Triage',
    toStatus: 'Complement',
    actorId: 'user_1',
    occurredAt: '2026-07-01T10:00:00Z',
    reason,
  }) as RequisitionTransition;

const req = (transitions: RequisitionTransition[]): Requisition => ({ transitions }) as Requisition;

describe('activeReason', () => {
  it('devolução corrente: mostra o motivo (última transição = ReturnForComplement)', () => {
    expect(activeReason(req([tr('Submit', null), tr('ReturnForComplement', 'ajustar data')]))).toEqual({
      action: 'ReturnForComplement',
      reason: 'ajustar data',
    });
  });

  // Antes o motivo escrito em "Solicitar ajuste nas parcelas" só existia no Histórico: o solicitante abria
  // o Complemento financeiro sem saber o que ajustar (o FinancialComplementForm não exibe motivo nenhum).
  it('ajuste nas parcelas: mostra o motivo (ReturnToFinancialComplement)', () => {
    expect(
      activeReason(req([tr('ApproveTriage', null), tr('ReturnToFinancialComplement', 'trocar vencimento')])),
    ).toEqual({ action: 'ReturnToFinancialComplement', reason: 'trocar vencimento' });
  });

  it('devolução já resolvida: não mostra (reenviada e liberada para aprovação)', () => {
    const transitions = [
      tr('Submit', null),
      tr('ReturnForComplement', 'ajustar data'),
      tr('Resubmit', null),
      tr('ApproveTriage', null),
    ];
    expect(activeReason(req(transitions))).toBeNull();
  });

  it('cancelamento (estado terminal): mostra o motivo', () => {
    expect(activeReason(req([tr('Submit', null), tr('Cancel', 'duplicada')]))).toEqual({
      action: 'Cancel',
      reason: 'duplicada',
    });
  });

  it('sem devolução/cancelamento na última transição: null', () => {
    expect(activeReason(req([tr('Submit', null)]))).toBeNull();
  });

  it('devolução na última transição, mas sem motivo: null', () => {
    expect(activeReason(req([tr('ReturnForComplement', null)]))).toBeNull();
  });
});

describe('transitionsToEvents', () => {
  const transition = (over: Partial<RequisitionTransition>): RequisitionTransition => ({
    action: 'Approve',
    fromStatus: 'Approval',
    toStatus: 'InPayment',
    actorId: 'user_a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6',
    actorName: null,
    actorPictureUrl: null,
    occurredAt: '2026-07-01T10:00:00Z',
    reason: null,
    ...over,
  });

  it('usa o nome resolvido pelo backend em vez do papel inferido', () => {
    const [event] = transitionsToEvents([transition({ actorName: 'Maria Silva' })]);

    expect(event.actorName).toBe('Maria Silva');
  });

  it('carrega a foto do ator para o avatar da timeline', () => {
    const picture = 'https://lh3.googleusercontent.com/a/maria';
    const [event] = transitionsToEvents([transition({ actorName: 'Maria Silva', actorPictureUrl: picture })]);

    expect(event.actorPictureUrl).toBe(picture);
  });

  // As listas não resolvem o ator (uma consulta de diretório por linha não se paga), então o fallback
  // por papel continua sendo o que aparece ali.
  it('cai para o papel quando o backend não resolveu o ator', () => {
    const [event] = transitionsToEvents([transition({ actorName: null })]);

    expect(event.actorName).toBe('Aprovador');
  });

  it('prefere "Você" ao nome quando o ator é o próprio usuário', () => {
    const [event] = transitionsToEvents(
      [transition({ actorName: 'Maria Silva' })],
      'user_a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6',
    );

    expect(event.actorName).toBe('Você');
  });
});
