import { describe, it, expect, vi } from 'vitest';
import { useState } from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { Combobox, type ComboboxItem } from './ui';
import './i18n';

const ITEMS: ComboboxItem[] = [
  { id: 'vendor_1', label: 'Google Brasil', sub: '00.000.000/0001-00' },
  { id: 'vendor_2', label: 'Microsoft', sub: '11.111.111/0001-11' },
  { id: 'vendor_3', label: 'Amazon AWS', sub: '22.222.222/0001-22' },
];

/** Controlled harness so we can assert the selected value after onChange. */
function Harness({ onChange, footer }: { onChange: (id: string | null) => void; footer?: boolean }) {
  const [value, setValue] = useState<string | null>(null);
  return (
    <Combobox
      value={value}
      onChange={(id) => {
        setValue(id);
        onChange(id);
      }}
      items={ITEMS}
      footer={footer ? <button onClick={() => onChange('__footer__')}>+ Novo</button> : undefined}
    />
  );
}

describe('Combobox (Radix Popover)', () => {
  it('filters items by the typed query', () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);

    fireEvent.click(screen.getByRole('button'));
    expect(screen.getAllByRole('option')).toHaveLength(3);

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'micro' } });
    const options = screen.getAllByRole('option');
    expect(options).toHaveLength(1);
    expect(options[0]).toHaveTextContent('Microsoft');
  });

  it('calls onChange with the chosen id and renders the label in the trigger', () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} />);

    fireEvent.click(screen.getByRole('button'));
    fireEvent.click(screen.getByRole('option', { name: /Amazon AWS/ }));

    expect(onChange).toHaveBeenCalledWith('vendor_3');
    expect(screen.getByRole('button')).toHaveTextContent('Amazon AWS');
  });

  it('fires the footer action', () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} footer />);

    fireEvent.click(screen.getByRole('button'));
    fireEvent.click(screen.getByRole('button', { name: '+ Novo' }));
    expect(onChange).toHaveBeenCalledWith('__footer__');
  });
});
