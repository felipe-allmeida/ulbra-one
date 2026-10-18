/** BR document/contact masking + validation helpers. UI strings live elsewhere; this is pure code. */

export const onlyDigits = (value: string): string => (value ?? '').replace(/\D/g, '');

/** CNPJ characters only, uppercased — keeps letters for the alphanumeric CNPJ (RFB, mandatory Jul/2026). */
export const normalizeCnpj = (value: string): string => (value ?? '').toUpperCase().replace(/[^0-9A-Z]/g, '');

/** 000.000.000-00 */
export function maskCpf(value: string): string {
  const d = onlyDigits(value).slice(0, 11);
  let out = d.slice(0, 3);
  if (d.length > 3) out += '.' + d.slice(3, 6);
  if (d.length > 6) out += '.' + d.slice(6, 9);
  if (d.length > 9) out += '-' + d.slice(9, 11);
  return out;
}

/** 00.000.000/0000-00 — accepts the alphanumeric CNPJ (letters in the first 12 positions). */
export function maskCnpj(value: string): string {
  const d = normalizeCnpj(value).slice(0, 14);
  let out = d.slice(0, 2);
  if (d.length > 2) out += '.' + d.slice(2, 5);
  if (d.length > 5) out += '.' + d.slice(5, 8);
  if (d.length > 8) out += '/' + d.slice(8, 12);
  if (d.length > 12) out += '-' + d.slice(12, 14);
  return out;
}

/** Formats a digits-only CPF/CNPJ for display (>11 digits → CNPJ mask, else CPF). */
export const fmtDoc = (doc: string): string => (doc.length > 11 ? maskCnpj(doc) : maskCpf(doc));

/** 00000-000 */
export function maskCep(value: string): string {
  const d = onlyDigits(value).slice(0, 8);
  return d.length > 5 ? d.slice(0, 5) + '-' + d.slice(5, 8) : d;
}

/** (00) 0000-0000 or (00) 00000-0000 */
export function maskPhone(value: string): string {
  const d = onlyDigits(value).slice(0, 11);
  if (d.length === 0) return '';
  let out = '(' + d.slice(0, 2);
  if (d.length > 2) out += ') ';
  if (d.length <= 2) return out;
  if (d.length <= 6) return out + d.slice(2);
  if (d.length <= 10) return out + d.slice(2, 6) + '-' + d.slice(6, 10);
  return out + d.slice(2, 7) + '-' + d.slice(7, 11);
}

/** Middle-truncates a file name to at most `max` chars, ALWAYS keeping its extension visible at the end, so a
 * long name (e.g. a generated CNAB remittance file) never pushes adjacent controls off-screen. The middle is
 * elided with `…`, keeping a head and a tail of the base so files that differ only near the end stay distinct.
 * The full name still belongs in a `title`/tooltip. */
export function truncateFileName(name: string, max = 40): string {
  if (!name || name.length <= max) return name ?? '';
  const ellipsis = '…';
  const dot = name.lastIndexOf('.');
  const ext = dot > 0 ? name.slice(dot) : ''; // ".txt" — empty when there is no extension
  const base = dot > 0 ? name.slice(0, dot) : name;
  const budget = max - ellipsis.length - ext.length;
  if (budget <= 1) return ellipsis + ext; // extension alone already fills the budget
  const head = Math.ceil(budget * 0.6);
  const tail = budget - head;
  return base.slice(0, head) + ellipsis + (tail > 0 ? base.slice(base.length - tail) : '') + ext;
}

/** Mirrors the backend DocumentValidator mod-11 check-digit algorithm. */
export function isValidCpf(value: string): boolean {
  const d = onlyDigits(value);
  if (d.length !== 11) return false;
  if (/^(\d)\1{10}$/.test(d)) return false;

  const calc = (len: number): number => {
    let sum = 0;
    for (let i = 0; i < len; i++) sum += Number(d[i]) * (len + 1 - i);
    const rest = (sum * 10) % 11;
    return rest === 10 ? 0 : rest;
  };
  return calc(9) === Number(d[9]) && calc(10) === Number(d[10]);
}

/** Validates numeric and alphanumeric CNPJ (RFB, Jul/2026): each char's mod-11 value is its char code minus 48. */
export function isValidCnpj(value: string): boolean {
  const s = normalizeCnpj(value);
  if (s.length !== 14) return false;
  if (!/^[0-9A-Z]{12}[0-9]{2}$/.test(s)) return false;
  if (/^(.)\1{13}$/.test(s)) return false;

  const val = (c: string): number => c.charCodeAt(0) - 48;
  const calc = (len: number): number => {
    const weights = len === 12 ? [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2] : [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    let sum = 0;
    for (let i = 0; i < len; i++) sum += val(s[i]) * weights[i];
    const rest = sum % 11;
    return rest < 2 ? 0 : 11 - rest;
  };
  return calc(12) === Number(s[12]) && calc(13) === Number(s[13]);
}
