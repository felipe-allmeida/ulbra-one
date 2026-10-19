import { describe, it, expect } from 'vitest';
import { boletoDigits, isBoletoLengthValid, isBoletoLineValid, isConvenioBoleto } from './boleto';

describe('boletoDigits', () => {
  it('mantém só os dígitos (remove pontos e espaços)', () => {
    expect(boletoDigits('34191.79001 01043.510047 91020.150008 2 91070026000')).toBe(
      '34191790010104351004791020150008291070026000',
    );
    expect(boletoDigits('')).toBe('');
  });
});

describe('isConvenioBoleto', () => {
  it('convênio/arrecadação quando começa com 8', () => {
    expect(isConvenioBoleto('846700000017 599200241114 202410000000 000000000000')).toBe(true);
  });

  it('boleto bancário (não convênio) quando não começa com 8', () => {
    expect(isConvenioBoleto('23793381286008271309005678911101234560000010000')).toBe(false);
    expect(isConvenioBoleto('00190.00009 01234.567890 12345.678901 2 34560000012345')).toBe(false);
  });

  it('ignora máscara e trata vazio como não-convênio', () => {
    expect(isConvenioBoleto('8.4670000-0017')).toBe(true);
    expect(isConvenioBoleto('')).toBe(false);
  });

  it('48 dígitos sem o prefixo 8 não é convênio (dado inválido, não bancário)', () => {
    expect(isConvenioBoleto('3' + '1'.repeat(47))).toBe(false);
  });
});

describe('isBoletoLineValid', () => {
  it('aceita 47 (bancário) e 48 começando com 8 (convênio)', () => {
    expect(isBoletoLineValid('23793381286008271309005678911101234560000010000')).toBe(true); // 47
    expect(isBoletoLineValid('846700000017599200241114202410000000000000000000')).toBe(true); // 48, lead 8
  });

  it('rejeita 48 dígitos sem o prefixo 8 (nem bancário nem convênio válido)', () => {
    expect(isBoletoLineValid('3' + '1'.repeat(47))).toBe(false);
  });

  it('rejeita 47 dígitos começando com 8 (convênio que perdeu um dígito — roteia p/ Seg O e estoura)', () => {
    expect(isBoletoLineValid('82650000007889180798000002026177663112901001043')).toBe(false); // 47, lead 8
  });

  it('rejeita tamanhos diferentes de 47/48 e linha vazia', () => {
    expect(isBoletoLineValid('123')).toBe(false);
    expect(isBoletoLineValid('')).toBe(false);
  });
});

describe('isBoletoLengthValid', () => {
  it('aceita 47 (bancário) e 48 (convênio) dígitos', () => {
    expect(isBoletoLengthValid('23793381286008271309005678911101234560000010000')).toBe(true); // 47
    expect(isBoletoLengthValid('846700000017599200241114202410000000000000000000')).toBe(true); // 48
  });

  it('rejeita tamanhos diferentes de 47/48', () => {
    expect(isBoletoLengthValid('123')).toBe(false);
    expect(isBoletoLengthValid('')).toBe(false);
    expect(isBoletoLengthValid('2379338128600827130900567891110123456000001000')).toBe(false); // 46
  });

  it('conta só os dígitos, ignorando a máscara', () => {
    expect(isBoletoLengthValid('23793.38128 60082.713090 05678.911101 2 34560000010000')).toBe(true); // 47 díg
  });
});
