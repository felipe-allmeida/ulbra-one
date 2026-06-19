import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import { useCustomers } from '../../queries';
import { Button, Combobox, Drawer } from '../../ui';
import { CustomerForm } from '../registry-forms';

/* ============ Customer (payer) picker (Combobox + inline create) ============ */
export function CustomerPicker({
  companyId,
  value,
  onChange,
  invalid,
}: {
  companyId: string;
  value: string | null;
  onChange: (id: string | null) => void;
  invalid?: boolean;
}) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [drawerOpen, setDrawerOpen] = useState(false);

  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(id);
  }, [search]);

  const customers = useCustomers(companyId, debouncedSearch);
  const items = (customers.data?.items ?? []).map((p) => ({
    id: p.id,
    label: p.name,
    sub: p.tradeName ?? p.document,
  }));

  return (
    <>
      <Combobox
        value={value}
        onChange={onChange}
        items={items}
        invalid={invalid}
        loading={customers.isLoading}
        placeholder={t('transactions.counterparty.selectCustomer')}
        searchPlaceholder={t('transactions.counterparty.searchCustomer')}
        emptyText={t('transactions.counterparty.emptyCustomer')}
        onSearch={setSearch}
        footer={
          <Button type='button' variant='ghost' icon={<Plus />} onClick={() => setDrawerOpen(true)}>
            {t('transactions.counterparty.newCustomer')}
          </Button>
        }
      />
      <Drawer
        open={drawerOpen}
        title={t('transactions.counterparty.newCustomerTitle')}
        onClose={() => setDrawerOpen(false)}
      >
        {drawerOpen && (
          <CustomerForm
            companyId={companyId}
            onSaved={(c) => {
              onChange(c.id);
              setDrawerOpen(false);
            }}
            onCancel={() => setDrawerOpen(false)}
          />
        )}
      </Drawer>
    </>
  );
}
