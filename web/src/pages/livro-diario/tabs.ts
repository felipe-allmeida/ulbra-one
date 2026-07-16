/** The two views of the Livro Diário, surfaced as tabs. Extensible: add a key here + a render
 *  branch in the layout. Persisted in the URL as `?tab=`. Planilha (columnar grid) is the primary
 *  view; Cronológico is the per-day grouped view. */
export type JournalTabKey = 'planilha' | 'cronologico';

export const TAB_ORDER: JournalTabKey[] = ['planilha', 'cronologico'];
export const DEFAULT_TAB: JournalTabKey = 'planilha';

export function isTabKey(v: string | null): v is JournalTabKey {
  return v != null && (TAB_ORDER as string[]).includes(v);
}

/** Spreadsheet display presets, applied as `.diario-planilha--<key>` modifiers (see index.css).
 *  Persisted in the URL as `?modelo=`. */
export type ViewModelKey = 'modelo1' | 'modelo2' | 'modelo3';

export const VIEW_MODEL_ORDER: ViewModelKey[] = ['modelo1', 'modelo2', 'modelo3'];
export const DEFAULT_VIEW_MODEL: ViewModelKey = 'modelo1';

export function isViewModelKey(v: string | null): v is ViewModelKey {
  return v != null && (VIEW_MODEL_ORDER as string[]).includes(v);
}
