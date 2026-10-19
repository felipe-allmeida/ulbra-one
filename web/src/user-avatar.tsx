import { useEffect, useState } from 'react';
import { cx } from './ui';
import { iniciais } from './lib';

/**
 * Foto do perfil Google do usuário, com as iniciais do nome como fallback. São DOIS fallbacks, não um: a
 * foto pode não existir (`pictureUrl` nulo) e a URL guardada pode ter expirado — a Google rotaciona o link
 * quando a pessoa troca a foto —, o que só se descobre quando o `onError` dispara.
 *
 * O tamanho e o formato vêm do `className` de quem chama (`.nav-rail-avatar`, `.mnav-avatar`, …); aqui só
 * mora o comportamento da imagem.
 */
export function UserAvatar({
  name,
  pictureUrl,
  className,
  title,
}: {
  name: string;
  pictureUrl?: string | null;
  className?: string;
  title?: string;
}) {
  const [failed, setFailed] = useState(false);

  // Uma foto nova (outro usuário na mesma posição da lista, ou o próprio depois de trocar a imagem) merece
  // uma tentativa — sem isto o componente ficaria preso nas iniciais desde a primeira falha.
  useEffect(() => setFailed(false), [pictureUrl]);

  return (
    <span className={cx('user-avatar', className)} title={title}>
      {pictureUrl && !failed ? (
        <img
          src={pictureUrl}
          alt={name}
          // A Google recusa a imagem quando o referrer não é o esperado, e a recusa chega como imagem quebrada.
          referrerPolicy='no-referrer'
          loading='lazy'
          onError={() => setFailed(true)}
        />
      ) : (
        iniciais(name)
      )}
    </span>
  );
}
