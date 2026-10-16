import { type ReactNode } from 'react';
import { Button } from '../ui';

/** Set key for a (company, child) pair. Ids never contain '|', so the join is unambiguous. */
export const pairKey = (companyId: string, childId: string) => `${companyId}|${childId}`;

export const parseUnitKey = (key: string) => {
  const [companyId, unitId] = key.split('|');
  return { companyId, unitId };
};

export const parseCcKey = (key: string) => {
  const [companyId, costCenterId] = key.split('|');
  return { companyId, costCenterId };
};

/** One labeled scope group with a "Selecionar todos" control. Shared by the users table (editing an existing
 * user's scope) and the registration dialog (setting it before the first sign-in). */
export function ScopeGroup({
  title,
  selectAllLabel,
  onSelectAll,
  children,
}: {
  title: string;
  selectAllLabel: string;
  onSelectAll: () => void;
  children: ReactNode;
}) {
  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 8, gap: 12 }}>
        <div className='section-title' style={{ margin: 0 }}>
          {title}
        </div>
        <Button variant='ghost' aria-label={selectAllLabel} onClick={onSelectAll}>
          Selecionar todos
        </Button>
      </div>
      {children}
    </div>
  );
}
