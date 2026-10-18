/**
 * Helpers da linha digitável do boleto no complemento financeiro (AEL-877). Espelham as regras que o backend
 * já aplica na geração da remessa CNAB — sem round-trip: a detecção de convênio e a validação de tamanho são
 * puramente estruturais sobre os dígitos.
 */

/** Só os dígitos da linha digitável (remove pontos, espaços e quaisquer separadores). */
export const boletoDigits = (line: string): string => line.replace(/\D/g, '');

/**
 * True quando a linha digitável é de convênio/arrecadação (concessionária, tributo) — o código de barras
 * começa com '8'. Espelha `BoletoBarcode.IsConvenio` (C#). Convênio → CNAB240 Segmento O; senão Segmento J.
 */
export const isConvenioBoleto = (line: string): boolean => boletoDigits(line).startsWith('8');

/** 47 dígitos (boleto bancário) ou 48 (convênio) — só o tamanho. */
export const isBoletoLengthValid = (line: string): boolean => {
  const n = boletoDigits(line).length;
  return n === 47 || n === 48;
};

/** Valid CNAB linha digitável (mirrors `CnabFieldValidation.BoletoReason`): bancário = 47 digits, lead not '8';
 * convênio = 48 digits, lead '8'. A 47 starting with '8' is a truncated convênio → routes to Seg O and throws. */
export const isBoletoLineValid = (line: string): boolean => {
  const n = boletoDigits(line).length;
  return (n === 47 && !isConvenioBoleto(line)) || (n === 48 && isConvenioBoleto(line));
};
