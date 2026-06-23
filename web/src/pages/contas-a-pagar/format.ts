import { maskCnpj, maskCpf } from '../../format';

export const fmtData = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');
/** Formats a digits-only CPF/CNPJ for display (14 digits → CNPJ mask, else CPF). */
export const fmtDoc = (doc: string) => (doc.length > 11 ? maskCnpj(doc) : maskCpf(doc));
/** Formats an ISO date-time (e.g. approvedAt/rejectedAt) as a pt-BR date. */
export const fmtDateTime = (iso: string) => new Date(iso).toLocaleDateString('pt-BR');

/** Local date → 'YYYY-MM-DD' (DateOnly wire format). Never toISOString() — UTC-3 would shift the day. */
export const toDateOnly = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
