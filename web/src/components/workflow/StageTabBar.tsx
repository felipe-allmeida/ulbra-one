import { cx } from '../../ui';

export interface StageTab {
  key: string;
  label: string;
  count?: number;
  badge?: 'warning' | 'danger';
}

/** Underline-style stage tabs. `label` is pre-translated; active tab controlled by the parent. */
export function StageTabBar({
  tabs,
  active,
  onChange,
}: {
  tabs: StageTab[];
  active: string;
  onChange: (key: string) => void;
}) {
  return (
    <div className='stage-tabbar' role='tablist'>
      {tabs.map((tab) => (
        <button
          key={tab.key}
          type='button'
          role='tab'
          aria-selected={tab.key === active}
          className={cx('stage-tab', tab.key === active && 'active')}
          onClick={() => onChange(tab.key)}
        >
          <span className='stage-tab-label'>{tab.label}</span>
          {tab.count != null && (
            <span className={cx('stage-tab-count', tab.badge && `count-${tab.badge}`)}>{tab.count}</span>
          )}
        </button>
      ))}
    </div>
  );
}
