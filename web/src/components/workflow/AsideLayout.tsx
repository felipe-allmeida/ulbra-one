import { type ReactNode } from 'react';

// Conteúdo principal + uma coluna secundária de 360px que fica FIXA ao rolar (>=1200px); abaixo disso
// vira coluna única e o aside desempilha para o fim da página. Nasceu nas "home por papel" (AEL-835) —
// daí o nome antigo `RoleHome` —, mas hoje serve também detalhe, formulário e inbox: o que ele descreve
// é a forma, não o papel. Escopado pelas próprias classes, então não afeta as telas de lista/tabela.
export function AsideLayout({
  children,
  aside,
  className,
}: {
  children: ReactNode;
  aside?: ReactNode;
  className?: string;
}) {
  return (
    <div className={`aside-layout${className ? ` ${className}` : ''}`}>
      <div className='aside-layout-main'>{children}</div>
      {aside && <aside className='aside-layout-side'>{aside}</aside>}
    </div>
  );
}
