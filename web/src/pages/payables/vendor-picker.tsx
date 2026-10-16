import { useEffect, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus } from 'lucide-react';
import { useVendors } from '../../queries';
import { Button, Combobox, Drawer } from '../../ui';
import { VendorForm } from '../registry-forms';
import { type VendorValues } from '../schemas';

/* ============ Vendor picker (Combobox + inline create) ============ */
export function VendorPicker({
  companyId,
  value,
  onChange,
  invalid,
  prefill,
  notice,
}: {
  companyId: string;
  value: string | null;
  onChange: (id: string | null) => void;
  invalid?: boolean;
  /** Pre-fill for the inline create form (e.g. issuer data from the NF when no vendor matched). */
  prefill?: Partial<VendorValues>;
  notice?: ReactNode;
}) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [drawerOpen, setDrawerOpen] = useState(false);

  useEffect(() => {
    const id = setTimeout(() => setDebouncedSearch(search), 250);
    return () => clearTimeout(id);
  }, [search]);

  // Vendors are global to the organization — the list loads regardless of the selected company.
  const vendors = useVendors(debouncedSearch);
  const items = (vendors.data?.items ?? []).map((p) => ({
    id: p.id,
    label: p.name,
    sub: p.tradeName ?? p.document,
  }));

  return (
    <>
      {/* Column layout so the prominent "register vendor" button never squeezes the combobox in a narrow
          grid column (e.g. the payable form's 3-col row) — it stacks below the field, left-aligned. */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8, minWidth: 0 }}>
        <Combobox
          value={value}
          onChange={onChange}
          items={items}
          invalid={invalid}
          loading={vendors.isLoading}
          placeholder={t('transactions.counterparty.selectVendor')}
          searchPlaceholder={t('transactions.counterparty.searchVendor')}
          emptyText={t('transactions.counterparty.emptyVendor')}
          onSearch={setSearch}
          footer={
            <Button type='button' variant='ghost' icon={<Plus />} onClick={() => setDrawerOpen(true)}>
              {t('transactions.counterparty.newVendor')}
            </Button>
          }
        />
        {/* When the NF issuer had no matching vendor, offer a prominent prefilled "register vendor". */}
        {prefill && (
          <Button
            type='button'
            variant='brand'
            icon={<Plus />}
            onClick={() => setDrawerOpen(true)}
            style={{ alignSelf: 'flex-start' }}
          >
            {t('payables.fiscalReader.registerVendor')}
          </Button>
        )}
      </div>
      <Drawer
        open={drawerOpen}
        title={t('transactions.counterparty.newVendorTitle')}
        onClose={() => setDrawerOpen(false)}
      >
        {drawerOpen && (
          <VendorForm
            companyId={companyId}
            prefill={prefill}
            notice={notice}
            onSaved={(v) => {
              onChange(v.id);
              setDrawerOpen(false);
            }}
            onCancel={() => setDrawerOpen(false)}
          />
        )}
      </Drawer>
    </>
  );
}
