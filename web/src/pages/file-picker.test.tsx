import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FilePicker } from './contas-a-pagar';
import '../i18n';

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
});
