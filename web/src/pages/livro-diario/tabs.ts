/** The two views of the Livro Diário, surfaced as tabs. Extensible: add a key here + a render
 *  branch in the layout. Persisted in the URL as `?tab=`. `spreadsheet` (columnar grid) is the
 *  primary view; `chronological` is the per-day grouped view. */
export type JournalTabKey = 'spreadsheet' | 'chronological';

export const TAB_ORDER: JournalTabKey[] = ['spreadsheet', 'chronological'];
export const DEFAULT_TAB: JournalTabKey = 'spreadsheet';

export function isTabKey(v: string | null): v is JournalTabKey {
  return v != null && (TAB_ORDER as string[]).includes(v);
}

/** Spreadsheet display presets, applied as `.diario-planilha--<key>` modifiers (see index.css).
 *  Persisted in the URL as `?view=`. */
export type ViewModelKey = 'model1' | 'model2' | 'model3';

export const VIEW_MODEL_ORDER: ViewModelKey[] = ['model1', 'model2', 'model3'];
export const DEFAULT_VIEW_MODEL: ViewModelKey = 'model1';

export function isViewModelKey(v: string | null): v is ViewModelKey {
  return v != null && (VIEW_MODEL_ORDER as string[]).includes(v);
}
