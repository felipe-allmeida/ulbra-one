import { useMemo } from 'react';
import { useCostCenters } from '../../queries';

/** Maps id→name of the company's cost centers (to show names in lists). */
export function useNames(companyId: string) {
  const centros = useCostCenters(companyId).data ?? [];
  return {
    centros,
    centroNome: useMemo(() => Object.fromEntries(centros.map((c) => [c.id, c.name])), [centros]),
  };
}
