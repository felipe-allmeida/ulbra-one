/** The pendency categories surfaced as tabs. Extensible: add a key here + a render branch in the layout. */
export type PendencyTabKey = 'approval' | 'fiscal';

export const TAB_ORDER: PendencyTabKey[] = ['approval', 'fiscal'];

export function isTabKey(v: string | null): v is PendencyTabKey {
  return v != null && (TAB_ORDER as string[]).includes(v);
}
