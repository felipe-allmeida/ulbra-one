import { useMemo } from 'react';
import { useCostCenters, useExpenseCategories } from '../../queries';

/** Maps id→name of the company's cost centers + expense categories (to show names in lists). */
export function useNames(companyId: string) {
  const centros = useCostCenters(companyId).data ?? [];
  const categorias = useExpenseCategories(true).data ?? [];
  return {
    centros,
    centroNome: useMemo(() => Object.fromEntries(centros.map((c) => [c.id, c.name])), [centros]),
    categoriaNome: useMemo(() => Object.fromEntries(categorias.map((c) => [c.id, c.name])), [categorias]),
  };
}
