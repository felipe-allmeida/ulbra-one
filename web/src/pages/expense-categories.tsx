import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Tag, Check, Pencil, Power, Trash2, X } from 'lucide-react';
import { type ExpenseCategory } from '../api';
import { useDeleteExpenseCategory, useExpenseCategories, useSaveExpenseCategory } from '../queries';
import { getErrorMessage } from '../apiError';
import { Button, Input, Field, PageHeader, Panel, EmptyState, Modal, ConfirmDialog, LoadingState } from '../ui';
import { ResponsiveTable, type Column } from '../components/ResponsiveTable';

export function ExpenseCategoriesPage() {
  const { t } = useTranslation();
  const list = useExpenseCategories(true);
  const categorias = list.data ?? [];

  const save = useSaveExpenseCategory();
  const remove = useDeleteExpenseCategory();

  const [createOpen, setCreateOpen] = useState(false);
  const [newName, setNewName] = useState('');
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState('');
  const [toDelete, setToDelete] = useState<ExpenseCategory | null>(null);
  const [createErr, setCreateErr] = useState<string | null>(null);
  const [deleteErr, setDeleteErr] = useState<string | null>(null);

  const openCreate = () => {
    setNewName('');
    setCreateErr(null);
    setCreateOpen(true);
  };

  const submitCreate = () => {
    setCreateErr(null);
    save.mutate(
      { name: newName.trim() },
      {
        onSuccess: () => setCreateOpen(false),
        onError: (e) => setCreateErr(getErrorMessage(e)),
      },
    );
  };

  const startRename = (c: ExpenseCategory) => {
    setEditingId(c.id);
    setEditName(c.name);
  };

  const submitRename = (c: ExpenseCategory) => {
    save.mutate({ id: c.id, name: editName.trim(), active: c.active }, { onSuccess: () => setEditingId(null) });
  };

  const toggleActive = (c: ExpenseCategory) => save.mutate({ id: c.id, name: c.name, active: !c.active });

  const submitDelete = () => {
    if (!toDelete) return;
    setDeleteErr(null);
    remove.mutate(
      { id: toDelete.id },
      {
        onSuccess: () => setToDelete(null),
        onError: (e) => setDeleteErr(getErrorMessage(e)),
      },
    );
  };

  return (
    <>
      <PageHeader title={t('expenseCategories.title')} sub={t('expenseCategories.subtitle')}>
        <Button variant='brand' icon={<Plus />} onClick={openCreate}>
          {t('expenseCategories.new')}
        </Button>
      </PageHeader>

      {list.isLoading ? (
        <Panel title={t('expenseCategories.title')}>
          <LoadingState />
        </Panel>
      ) : categorias.length === 0 ? (
        <EmptyState icon={<Tag />} title={t('expenseCategories.empty.title')} sub={t('expenseCategories.empty.sub')} />
      ) : (
        <Panel title={t('expenseCategories.count', { count: categorias.length })}>
          <ExpenseCategoriesTable
            categorias={categorias}
            editingId={editingId}
            editName={editName}
            setEditName={setEditName}
            startRename={startRename}
            submitRename={submitRename}
            cancelRename={() => setEditingId(null)}
            toggleActive={toggleActive}
            onDelete={(c) => {
              setDeleteErr(null);
              setToDelete(c);
            }}
          />
        </Panel>
      )}

      {createOpen && (
        <Modal
          title={t('expenseCategories.newTitle')}
          onClose={() => setCreateOpen(false)}
          footer={
            <>
              <Button onClick={() => setCreateOpen(false)}>{t('forms.actions.cancel')}</Button>
              <Button variant='brand' disabled={!newName.trim() || save.isPending} onClick={submitCreate}>
                {save.isPending ? t('forms.actions.saving') : t('forms.actions.create')}
              </Button>
            </>
          }
        >
          <Field label={t('expenseCategories.nameLabel')}>
            <Input
              autoFocus
              value={newName}
              placeholder={t('expenseCategories.namePlaceholder')}
              onChange={(e) => setNewName(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && newName.trim()) submitCreate();
              }}
            />
          </Field>
          {createErr && <div className='create-err'>{createErr}</div>}
        </Modal>
      )}

      {toDelete && (
        <ConfirmDialog
          title={t('expenseCategories.deleteTitle')}
          danger
          busy={remove.isPending}
          confirmLabel={t('expenseCategories.delete')}
          message={
            <div>
              <div>{t('expenseCategories.deleteMessage', { name: toDelete.name })}</div>
              {deleteErr && (
                <div className='create-err' style={{ marginTop: 8 }}>
                  {deleteErr}
                </div>
              )}
            </div>
          }
          onConfirm={submitDelete}
          onCancel={() => setToDelete(null)}
        />
      )}
    </>
  );
}

/**
 * Ordem = nome/status/ações, igual a hoje. `nome` (edita inline) é o `title`; `status` é badge;
 * `ações` ganha `align: 'right'` — o `<td>` original levava `.td-action` (width:1%,
 * sem quebra, alinhado à direita) e `header: ''` porque a tabela nunca teve `<thead>` (`hideHeader`).
 * Sem `onRowClick` — a linha nunca navegou.
 */
function ExpenseCategoriesTable({
  categorias,
  editingId,
  editName,
  setEditName,
  startRename,
  submitRename,
  cancelRename,
  toggleActive,
  onDelete,
}: {
  categorias: ExpenseCategory[];
  editingId: string | null;
  editName: string;
  setEditName: (v: string) => void;
  startRename: (c: ExpenseCategory) => void;
  submitRename: (c: ExpenseCategory) => void;
  cancelRename: () => void;
  toggleActive: (c: ExpenseCategory) => void;
  onDelete: (c: ExpenseCategory) => void;
}) {
  const { t } = useTranslation();
  const columns: Column<ExpenseCategory>[] = [
    {
      key: 'name',
      header: t('expenseCategories.nameLabel'),
      role: 'title',
      cell: (c) =>
        editingId === c.id ? (
          <Input
            autoFocus
            value={editName}
            onChange={(e) => setEditName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') submitRename(c);
              if (e.key === 'Escape') cancelRename();
            }}
          />
        ) : (
          c.name
        ),
    },
    {
      key: 'status',
      header: 'Status',
      role: 'status',
      cell: (c) => (
        <span className={`badge-status ${c.active ? 'badge-aprovado' : 'badge-rejeitado'}`}>
          {c.active ? t('expenseCategories.active') : t('expenseCategories.inactive')}
        </span>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      align: 'right',
      cell: (c) => (
        // Ações de linha só-ícone (rótulo em aria-label), o mesmo `.icon-btn` das tabelas de cadastro.
        // Salvar/Cancelar do rename inline continuam com o `.btn` rotulado: são a confirmação de uma edição
        // aberta, não uma ação repetida por linha — só aparecem na linha que está sendo editada.
        <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
          {editingId === c.id ? (
            <>
              <Button className='btn-approve' icon={<Check />} onClick={() => submitRename(c)}>
                {t('forms.actions.save')}
              </Button>
              <Button icon={<X />} onClick={cancelRename}>
                {t('forms.actions.cancel')}
              </Button>
            </>
          ) : (
            <>
              <button
                className='btn btn-icon'
                onClick={() => startRename(c)}
                aria-label={t('expenseCategories.rename')}
                title={t('expenseCategories.rename')}
              >
                <Pencil />
              </button>
              <button
                className='btn btn-icon'
                onClick={() => toggleActive(c)}
                aria-label={c.active ? t('expenseCategories.deactivate') : t('expenseCategories.activate')}
                title={c.active ? t('expenseCategories.deactivate') : t('expenseCategories.activate')}
              >
                <Power />
              </button>
              <button
                className='btn btn-icon'
                onClick={() => onDelete(c)}
                aria-label={t('expenseCategories.delete')}
                title={t('expenseCategories.delete')}
              >
                <Trash2 />
              </button>
            </>
          )}
        </div>
      ),
    },
  ];

  return <ResponsiveTable rows={categorias} columns={columns} rowKey={(c) => c.id} hideHeader />;
}
