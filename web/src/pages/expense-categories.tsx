import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Tag, Check, Pencil, Trash2, X } from 'lucide-react';
import { type ExpenseCategory } from '../api';
import { useDeleteExpenseCategory, useExpenseCategories, useSaveExpenseCategory } from '../queries';
import { getErrorMessage } from '../apiError';
import { Button, Input, Field, PageHeader, Panel, EmptyState, Modal, ConfirmDialog, SkeletonRows } from '../ui';

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
          <table>
            <tbody>
              <SkeletonRows rows={5} cols={3} />
            </tbody>
          </table>
        </Panel>
      ) : categorias.length === 0 ? (
        <EmptyState icon={<Tag />} title={t('expenseCategories.empty.title')} sub={t('expenseCategories.empty.sub')} />
      ) : (
        <Panel title={t('expenseCategories.count', { count: categorias.length })}>
          <table>
            <tbody>
              {categorias.map((c) => (
                <tr key={c.id}>
                  <td>
                    {editingId === c.id ? (
                      <Input
                        autoFocus
                        value={editName}
                        onChange={(e) => setEditName(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') submitRename(c);
                          if (e.key === 'Escape') setEditingId(null);
                        }}
                      />
                    ) : (
                      c.name
                    )}
                  </td>
                  <td>
                    <span className={`badge-status ${c.active ? 'badge-aprovado' : 'badge-rejeitado'}`}>
                      {c.active ? t('expenseCategories.active') : t('expenseCategories.inactive')}
                    </span>
                  </td>
                  <td className='td-action'>
                    <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                      {editingId === c.id ? (
                        <>
                          <Button className='btn-approve' icon={<Check />} onClick={() => submitRename(c)}>
                            {t('forms.actions.save')}
                          </Button>
                          <Button icon={<X />} onClick={() => setEditingId(null)}>
                            {t('forms.actions.cancel')}
                          </Button>
                        </>
                      ) : (
                        <>
                          <Button icon={<Pencil />} onClick={() => startRename(c)}>
                            {t('expenseCategories.rename')}
                          </Button>
                          <Button onClick={() => toggleActive(c)}>
                            {c.active ? t('expenseCategories.deactivate') : t('expenseCategories.activate')}
                          </Button>
                          <Button
                            icon={<Trash2 />}
                            onClick={() => {
                              setDeleteErr(null);
                              setToDelete(c);
                            }}
                          >
                            {t('expenseCategories.delete')}
                          </Button>
                        </>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
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
