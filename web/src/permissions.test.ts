import { describe, it, expect } from 'vitest';
import { canSeeConsolidated, type Me } from './permissions';

const me = (isAdmin: boolean, role?: string) => ({ isAdmin, role }) as unknown as Me;

describe('canSeeConsolidated (AEL-906)', () => {
  it('true for the management roles (Admin/Presidency/Controllership/Finance/Accounting)', () => {
    expect(canSeeConsolidated(me(true))).toBe(true); // Admin (by flag)
    for (const role of ['Presidency', 'Controllership', 'Finance', 'Accounting']) {
      expect(canSeeConsolidated(me(false, role))).toBe(true);
    }
  });

  it('false for the scoped roles and the plain User', () => {
    for (const role of ['Requester', 'Approver', 'User']) {
      expect(canSeeConsolidated(me(false, role))).toBe(false);
    }
  });

  it('false when there is no user', () => {
    expect(canSeeConsolidated(null)).toBe(false);
    expect(canSeeConsolidated(undefined)).toBe(false);
  });
});
