import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ConfirmDialog, Drawer } from './ui';

describe('ConfirmDialog (Radix AlertDialog)', () => {
  it('renders with role="alertdialog" and fires onConfirm on the confirm button', () => {
    const onConfirm = vi.fn();
    const onCancel = vi.fn();
    render(
      <ConfirmDialog
        title='Excluir'
        message='Tem certeza?'
        confirmLabel='Confirmar'
        cancelLabel='Cancelar'
        danger
        onConfirm={onConfirm}
        onCancel={onCancel}
      />,
    );

    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Confirmar' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: 'Cancelar' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });
});

describe('Drawer (Radix Dialog)', () => {
  it('renders a dialog with title when open and nothing when closed', () => {
    const onClose = vi.fn();
    const { rerender } = render(
      <Drawer open={false} title='Novo fornecedor' onClose={onClose}>
        <p>conteúdo</p>
      </Drawer>,
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    rerender(
      <Drawer open title='Novo fornecedor' onClose={onClose}>
        <p>conteúdo</p>
      </Drawer>,
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Novo fornecedor')).toBeInTheDocument();
  });
});
