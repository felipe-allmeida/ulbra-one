import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FilePicker } from './ui';
import './i18n';

/** jsdom's DragEvent lacks a real dataTransfer; fire a `drop` with a stubbed file list. */
function fireDrop(el: Element, files: File[]) {
  fireEvent.drop(el, { dataTransfer: { files, items: [], types: ['Files'] } });
}

/**
 * Regression for the "load → clear → reload the SAME file" bug: a native <input type=file> does not
 * fire `change` when the selected file is identical to its current value. FilePicker must reset the
 * input value after each selection so re-picking the same file still triggers onSelect.
 */
describe('FilePicker', () => {
  it('resets the input value after a selection so the same file can be re-selected', async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    const { container } = render(<FilePicker accept='.xml' placeholder='Selecione' onSelect={onSelect} />);

    const input = container.querySelector<HTMLInputElement>('input[type="file"]')!;
    const file = new File(['<nfeProc/>'], 'NF1.xml', { type: 'text/xml' });

    await user.upload(input, file);

    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect.mock.calls[0][0]?.name).toBe('NF1.xml');
    // The value is cleared, so a subsequent selection of the same file would emit `change` again.
    expect(input.value).toBe('');
  });

  it('attaches a dropped file that matches accept', () => {
    const onSelect = vi.fn();
    const { container } = render(<FilePicker accept='.xml,.pdf' placeholder='Selecione' onSelect={onSelect} />);
    const label = container.querySelector('label')!;

    fireDrop(label, [new File(['<nfeProc/>'], 'NF1.xml', { type: 'text/xml' })]);

    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onSelect.mock.calls[0][0]?.name).toBe('NF1.xml');
  });

  it('ignores a dropped file whose type is not in accept', () => {
    const onSelect = vi.fn();
    const { container } = render(<FilePicker accept='.xml,.pdf' placeholder='Selecione' onSelect={onSelect} />);
    const label = container.querySelector('label')!;

    fireDrop(label, [new File(['x'], 'photo.png', { type: 'image/png' })]);

    expect(onSelect).not.toHaveBeenCalled();
  });
});
