import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';

/**
 * FakeBank — a first-class test double for "the bank" in the e2e full-flow suite.
 *
 * The bank does two things for the system: it PRODUCES a statement and it CONFIRMS a
 * movement. Today the system ingests the bank via OFX -> conciliação; tomorrow via a
 * gateway/Open Finance. FakeBank models the v1 (no-gateway) reality: it GENERATES a valid
 * OFXSGML statement that the real jcoliz.OfxSharp parser accepts, which the Conciliação UI
 * then consumes. When a real gateway arrives it slots in behind the same seam without
 * rewriting the e2e (which drive the UI).
 *
 * The OFX shape below is copied tag-for-tag from the format the parser is proven to accept
 * (tests/Ulbra.One.UnitTests/Finance/OfxStatementParserTests.cs and
 * tests/Ulbra.One.IntegrationTests/Finance/ConciliationEndpointsTests.cs):
 * header `OFXHEADER:100 / DATA:OFXSGML / VERSION:102`, then a
 * <BANKMSGSRSV1><STMTTRNRS><STMTRS> with <BANKTRANLIST><DTSTART><DTEND> and <STMTTRN>
 * blocks `<TRNTYPE>…<DTPOSTED>YYYYMMDD<TRNAMT>-NN.NN<FITID>unique<MEMO>text`.
 */

/** A single statement line. `amount` is signed: negative = debit (payment out), positive = credit. */
export interface OfxEntry {
  fitid: string;
  /** Posting date as a JS Date or an ISO 'YYYY-MM-DD' string. */
  date: Date | string;
  /** Signed amount: negative = debit, positive = credit. */
  amount: number;
  memo: string;
}

export interface OfxStatementOptions {
  entries: OfxEntry[];
  /** Statement period start (defaults to the earliest entry date, minus a day of slack). */
  periodStart?: Date | string;
  /** Statement period end (defaults to the latest entry date, plus a day of slack). */
  periodEnd?: Date | string;
}

/** Local date -> 'YYYYMMDD' (OFX DTPOSTED format). Never toISOString() — UTC could shift the day. */
function toOfxDate(d: Date | string): string {
  const date = typeof d === 'string' ? new Date(d + 'T00:00:00') : d;
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${y}${m}${day}`;
}

/** Two-decimal signed amount as the parser expects, e.g. -1000.00 / 2500.00. */
function toTrnAmt(amount: number): string {
  return amount.toFixed(2);
}

/** Builds one <STMTTRN> block. Debit when amount < 0, else credit. */
function stmtTrn(entry: OfxEntry): string {
  const type = entry.amount < 0 ? 'DEBIT' : 'CREDIT';
  return (
    `<STMTTRN><TRNTYPE>${type}<DTPOSTED>${toOfxDate(entry.date)}` +
    `<TRNAMT>${toTrnAmt(entry.amount)}<FITID>${entry.fitid}<MEMO>${entry.memo}</STMTTRN>`
  );
}

/**
 * Generates a valid OFXSGML statement string the jcoliz.OfxSharp parser accepts.
 * The header + SIGNON + STMTRS envelope are fixed; only the period and STMTTRN lines vary.
 */
export function ofxStatement(opts: OfxStatementOptions): string {
  const { entries } = opts;
  if (entries.length === 0) throw new Error('ofxStatement: at least one entry is required.');

  // Default the period to span all entries (with a day of slack) when not given explicitly.
  const dates = entries.map((e) => (typeof e.date === 'string' ? new Date(e.date + 'T00:00:00') : e.date));
  const earliest = new Date(Math.min(...dates.map((d) => d.getTime())));
  const latest = new Date(Math.max(...dates.map((d) => d.getTime())));
  const slack = (d: Date, days: number) => new Date(d.getFullYear(), d.getMonth(), d.getDate() + days);
  const dtStart = toOfxDate(opts.periodStart ?? slack(earliest, -1));
  const dtEnd = toOfxDate(opts.periodEnd ?? slack(latest, 1));

  const lines = entries.map(stmtTrn).join('\n');

  // Tag structure mirrors the parser-proven fixtures exactly.
  return [
    'OFXHEADER:100',
    'DATA:OFXSGML',
    'VERSION:102',
    'SECURITY:NONE',
    'ENCODING:USASCII',
    '',
    '<OFX>',
    '<SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>' +
      dtEnd +
      '<LANGUAGE>POR<FI><ORG>BANCO<FID>033</FI></SONRS></SIGNONMSGSRSV1>',
    '<BANKMSGSRSV1><STMTTRNRS><TRNUID>0<STATUS><CODE>0<SEVERITY>INFO</STATUS><STMTRS>',
    '<CURDEF>BRL',
    '<BANKACCTFROM><BANKID>033<ACCTID>12345<ACCTTYPE>CHECKING</BANKACCTFROM>',
    '<BANKTRANLIST>',
    `<DTSTART>${dtStart}`,
    `<DTEND>${dtEnd}`,
    lines,
    '</BANKTRANLIST>',
    `<LEDGERBAL><BALAMT>0.00<DTASOF>${dtEnd}</LEDGERBAL>`,
    '</STMTRS></STMTTRNRS></BANKMSGSRSV1>',
    '</OFX>',
    '',
  ].join('\n');
}

export interface DebitMatchingOptions {
  /** Positive magnitude of the installment to pay; the STMTTRN's TRNAMT becomes its negation. */
  amount: number;
  /** Installment due date (Date or ISO 'YYYY-MM-DD'); used verbatim as DTPOSTED (within the ±5d window). */
  dueDate: Date | string;
  /** Optional FITID; a unique one is generated per call when omitted (dedup key for re-imports). */
  fitid?: string;
  /** Optional MEMO; a descriptive default is used otherwise. */
  memo?: string;
}

/**
 * Builds a single matching-debit statement: TRNAMT = -amount, DTPOSTED = dueDate (inside the
 * matcher's ±5d window), with a unique FITID so re-imports dedup. The matcher suggests when
 * |TRNAMT| == the installment's remaining balance and the candidate is unique — callers ensure
 * uniqueness by paying a distinctive amount.
 */
export function debitMatching(opts: DebitMatchingOptions): string {
  const fitid = opts.fitid ?? uniqueFitid();
  const memo = opts.memo ?? 'PAGTO FORNECEDOR FAKEBANK';
  return ofxStatement({ entries: [{ fitid, date: opts.dueDate, amount: -Math.abs(opts.amount), memo }] });
}

/** A per-run-unique FITID so re-imports dedup against prior runs on the persistent dev DB. */
export function uniqueFitid(prefix = 'FAKEBANK'): string {
  const rand = Math.random().toString(36).slice(2, 8).toUpperCase();
  return `${prefix}-${Date.now()}-${rand}`;
}

/** Writes OFX content to a temp file under the OS temp dir and returns its path for setInputFiles. */
export function writeOfxFile(content: string): string {
  const dir = mkdtempSync(join(tmpdir(), 'fakebank-'));
  const path = join(dir, `extrato-${Date.now()}.ofx`);
  // ASCII matches the parser fixtures (ENCODING:USASCII) and avoids BOM surprises.
  writeFileSync(path, content, { encoding: 'ascii' });
  return path;
}
