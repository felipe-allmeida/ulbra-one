import { useTranslation } from 'react-i18next';
import { Chip } from '../ui';
import { UserAvatar } from '../user-avatar';
import { useMe } from '../queries';
import { usePresence } from './usePresence';

const MAX_SHOWN = 3;

/**
 * Compara ids "de bate": `userId` de presença chega cru (`Guid.ToString()`, com hífen) e `me.userId`
 * chega prefixado (`user_{guid:N}`) — mesmo problema (e mesma solução) de `timeline.ts`.
 */
function sameUser(a: string, b: string): boolean {
  const bare = (id: string) =>
    id
      .replace(/^[a-z]+_/, '')
      .replace(/-/g, '')
      .toLowerCase();
  return bare(a) === bare(b);
}

/**
 * Quem está vendo este recurso agora, VOCÊ INCLUSIVE — dois discos anônimos não deixam claro quem está
 * contado, então o próprio usuário vem primeiro e rotulado. Lista vazia não renderiza nada: é o que
 * acontece quando o backplane está fora, e presença é acessório (some sem quebrar a tela).
 */
export function PresenceAvatars({ scope, resourceId }: { scope: string; resourceId: string }) {
  const { t } = useTranslation();
  const { users } = usePresence(scope, resourceId);
  const me = useMe().data;
  if (users.length === 0) return null;

  const isMe = (u: { userId: string }) => !!me && sameUser(u.userId, me.userId);
  const ordered = [...users.filter(isMe), ...users.filter((u) => !isMe(u))];

  const shown = ordered.slice(0, MAX_SHOWN);
  const extra = ordered.length - shown.length;

  return (
    <>
      {shown.map((u) => (
        <Chip key={u.userId} tone='brand'>
          <UserAvatar
            className='presence-avatar'
            name={u.name}
            pictureUrl={u.pictureUrl}
            title={isMe(u) ? t('common.you') : u.name}
          />
        </Chip>
      ))}
      {extra > 0 && <Chip>+{extra}</Chip>}
    </>
  );
}
