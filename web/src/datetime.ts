export const fmtDate = (iso: string) => new Date(iso + 'T00:00:00').toLocaleDateString('pt-BR');

/** Formats an ISO date-time (e.g. approvedAt/rejectedAt) as a pt-BR date. */
export const fmtDateTime = (iso: string) => new Date(iso).toLocaleDateString('pt-BR');

/** Local date → 'YYYY-MM-DD' (DateOnly wire format). Never toISOString() — UTC-3 would shift the day. */
export const toDateOnly = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
