import '@testing-library/jest-dom';
import { beforeEach, vi } from 'vitest';

/**
 * jsdom não implementa matchMedia, e `useMediaQuery` depende dele. O mock começa
 * sempre em `false` (= desktop), então um teste de componente sem configuração
 * explícita exercita o caminho de tabela. Testes de mobile sobrescrevem com
 * `setViewportMatches(true)`.
 *
 * Mock objects são estáveis por query para permitir espiagem de addEventListener/removeEventListener.
 * setViewportMatches também dispara eventos 'change' para atualizar hooks subscritos.
 */
let currentMatches = false;

export interface MockMediaQueryList extends Omit<MediaQueryList, 'matches'> {
  matches: boolean;
  listeners?: Set<EventListener>;
}

const mockObjects = new Map<string, MockMediaQueryList>();

export function setViewportMatches(matches: boolean) {
  currentMatches = matches;
  mockObjects.forEach((obj) => {
    obj.matches = matches;
    // Dispara evento 'change' para todos os listeners subscritos
    if (obj.listeners) {
      const event = new Event('change');
      obj.listeners.forEach((listener) => listener(event));
    }
  });
}

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => {
    if (!mockObjects.has(query)) {
      const listeners = new Set<EventListener>();
      mockObjects.set(query, {
        matches: currentMatches,
        media: query,
        listeners,
        onchange: null,
        addEventListener: vi.fn((event: string, handler: EventListener) => {
          if (event === 'change') {
            listeners.add(handler);
          }
        }),
        removeEventListener: vi.fn((event: string, handler: EventListener) => {
          if (event === 'change') {
            listeners.delete(handler);
          }
        }),
        dispatchEvent: () => false,
        addListener: () => undefined,
        removeListener: () => undefined,
      } as MockMediaQueryList);
    }
    return mockObjects.get(query)!;
  },
});

beforeEach(() => {
  currentMatches = false;
  mockObjects.clear(); // Isola os testes entre si
});
