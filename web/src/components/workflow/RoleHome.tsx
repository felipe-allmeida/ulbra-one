import { type ReactNode } from 'react';

// Two-column role home: main content + a sticky aside. Collapses to a single column on narrow
// screens. Reused across the per-role home screens of the redesign (AEL-835). Scoped by its own
// classes so it never affects the feature list/table screens.
export function RoleHome({
  children,
  aside,
  className,
}: {
  children: ReactNode;
  aside?: ReactNode;
  className?: string;
}) {
  return (
    <div className={`role-home${className ? ` ${className}` : ''}`}>
      <div className='role-home-main'>{children}</div>
      {aside && <aside className='role-home-aside'>{aside}</aside>}
    </div>
  );
}
