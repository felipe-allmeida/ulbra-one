import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { CheckCircle2, AlertCircle, X } from 'lucide-react';

type ToastKind = 'success' | 'error';
interface ToastItem {
  id: number;
  kind: ToastKind;
  message: string;
}

interface ToastApi {
  success: (message: string) => void;
  error: (message: string) => void;
}

const Ctx = createContext<ToastApi>(null!);
export const useToast = () => useContext(Ctx);

const DISMISS_MS = 4000;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);
  const seq = useRef(0);

  const remove = useCallback((id: number) => {
    setItems((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const push = useCallback(
    (kind: ToastKind, message: string) => {
      const id = ++seq.current;
      setItems((prev) => [...prev, { id, kind, message }]);
      setTimeout(() => remove(id), DISMISS_MS);
    },
    [remove],
  );

  const api: ToastApi = {
    success: useCallback((m: string) => push('success', m), [push]),
    error: useCallback((m: string) => push('error', m), [push]),
  };

  return (
    <Ctx.Provider value={api}>
      {children}
      <div className='toast-stack' role='status' aria-live='polite'>
        {items.map((t) => (
          <div key={t.id} className={`toast toast-${t.kind}`}>
            <span className='toast-icon'>{t.kind === 'success' ? <CheckCircle2 /> : <AlertCircle />}</span>
            <span className='toast-msg'>{t.message}</span>
            <button className='toast-close' onClick={() => remove(t.id)} aria-label='Fechar'>
              <X />
            </button>
          </div>
        ))}
      </div>
    </Ctx.Provider>
  );
}
