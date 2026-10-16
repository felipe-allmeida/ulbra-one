import { describe, it, expect, vi } from 'vitest';
import { useState } from 'react';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { Combobox, Drawer, type ComboboxItem } from './ui';
import './i18n';

const ITEMS: ComboboxItem[] = [
  { id: 'vendor_1', label: 'Google Brasil', sub: '00.000.000/0001-00' },
  { id: 'vendor_2', label: 'Microsoft', sub: '11.111.111/0001-11' },
  { id: 'vendor_3', label: 'Amazon AWS', sub: '22.222.222/0001-22' },
];

/** Controlled harness so we can assert the selected value after onChange. */
function Harness({
  onChange,
  footer,
  placeholder,
}: {
  onChange: (id: string | null) => void;
  footer?: boolean;
  placeholder?: string;
}) {
  const [value, setValue] = useState<string | null>(null);
  return (
    <Combobox
      value={value}
      onChange={(id) => {
        setValue(id);
        onChange(id);
      }}
      items={ITEMS}
      placeholder={placeholder}
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

  it('filters case- and accent-insensitively (pt-BR data)', () => {
    function AccentHarness() {
      const [value, setValue] = useState<string | null>(null);
      return (
        <Combobox
          value={value}
          onChange={setValue}
          items={[
            { id: 'a', label: 'Órgão Público' },
            { id: 'b', label: 'Microsoft' },
          ]}
        />
      );
    }
    render(<AccentHarness />);

    fireEvent.click(screen.getByRole('button'));
    // Lowercase and without diacritics still matches "Órgão Público".
    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'orgao publico' } });
    const options = screen.getAllByRole('option');
    expect(options).toHaveLength(1);
    expect(options[0]).toHaveTextContent('Órgão Público');
  });

  it('portals the search field inside a Drawer so its focus-trap does not steal keystrokes', () => {
    render(
      <Drawer open title='Cadastro' onClose={() => {}}>
        <Harness onChange={vi.fn()} placeholder='Selecionar' />
      </Drawer>,
    );
    const dialog = screen.getByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Selecionar' }));
    // The popover (search input) must live INSIDE the dialog subtree — not portaled to document.body —
    // otherwise the modal focus-trap reclaims focus and typing is dropped.
    expect(within(dialog).getByRole('combobox')).toBeInTheDocument();
  });

  it('fires the footer action', () => {
    const onChange = vi.fn();
    render(<Harness onChange={onChange} footer />);

    fireEvent.click(screen.getByRole('button'));
    fireEvent.click(screen.getByRole('button', { name: '+ Novo' }));
    expect(onChange).toHaveBeenCalledWith('__footer__');
  });
});
