import { useCallback, useSyncExternalStore } from 'react';

/**
 * Assina uma media query. `useSyncExternalStore` evita o flash de layout errado
 * que um `useState` + `useEffect` produziria no primeiro paint.
 */
export function useMediaQuery(query: string): boolean {
  // `subscribe` precisa de identidade estável entre renders: `useSyncExternalStore` re-executa
  // subscribe (removendo o listener antigo e adicionando um novo) sempre que recebe uma função
  // diferente. Uma arrow inline seria recriada a cada render — com ~90 tabelas migradas para
  // `ResponsiveTable` mais o chrome do app consumindo este hook, isso vira um
  // remove/addEventListener em `matchMedia` a cada render de cada consumidor. `useCallback`
  // travado em `query` mantém a mesma função entre renders (a query de cada chamador é estável).
  const subscribe = useCallback(
    (onChange: () => void) => {
      const mql = window.matchMedia(query);
      mql.addEventListener('change', onChange);
      return () => mql.removeEventListener('change', onChange);
    },
    [query],
  );

  return useSyncExternalStore(
    subscribe,
    () => window.matchMedia(query).matches,
    () => false, // server/snapshot: assume desktop
  );
}

/** O breakpoint de telefone do app. Mantenha em sincronia com os 640px do index.css. */
export const usePhone = () => useMediaQuery('(max-width: 640px)');
