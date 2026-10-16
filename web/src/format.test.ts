import { describe, it, expect } from 'vitest';
import { onlyDigits, normalizeCnpj, maskCpf, maskCnpj, maskCep, maskPhone, isValidCpf, isValidCnpj } from './format';

describe('onlyDigits', () => {
  it('strips non-digits', () => {
    expect(onlyDigits('529.982.247-25')).toBe('52998224725');
    expect(onlyDigits('')).toBe('');
  });
});

describe('masks', () => {
  it('masks CPF progressively', () => {
    expect(maskCpf('529')).toBe('529');
    expect(maskCpf('52998224725')).toBe('529.982.247-25');
    expect(maskCpf('5299822472599')).toBe('529.982.247-25'); // caps at 11 digits
  });
  it('masks CNPJ', () => {
    expect(maskCnpj('11222333000181')).toBe('11.222.333/0001-81');
  });
  it('masks alphanumeric CNPJ (letters in the first 12 positions)', () => {
    expect(maskCnpj('12ABC34501DE35')).toBe('12.ABC.345/01DE-35');
    expect(maskCnpj('12abc34501de35')).toBe('12.ABC.345/01DE-35'); // uppercases
  });
  it('masks CEP', () => {
    expect(maskCep('01001000')).toBe('01001-000');
    expect(maskCep('010')).toBe('010');
  });
  it('masks phone (8 and 9 digit)', () => {
    expect(maskPhone('1133334444')).toBe('(11) 3333-4444');
    expect(maskPhone('11999998888')).toBe('(11) 99999-8888');
  });
});

describe('isValidCpf', () => {
  it('accepts a valid CPF formatted or raw', () => {
    expect(isValidCpf('529.982.247-25')).toBe(true);
    expect(isValidCpf('52998224725')).toBe(true);
  });
  it('rejects invalid CPFs', () => {
    expect(isValidCpf('111.111.111-11')).toBe(false);
    expect(isValidCpf('12345678900')).toBe(false);
    expect(isValidCpf('123')).toBe(false);
    expect(isValidCpf('')).toBe(false);
  });
});

describe('normalizeCnpj', () => {
  it('keeps alphanumerics and uppercases', () => {
    expect(normalizeCnpj('12.ABC.345/01DE-35')).toBe('12ABC34501DE35');
    expect(normalizeCnpj('12abc34501de35')).toBe('12ABC34501DE35');
    expect(normalizeCnpj('11.222.333/0001-81')).toBe('11222333000181');
  });
});

describe('isValidCnpj', () => {
  it('accepts a valid numeric CNPJ formatted or raw', () => {
    expect(isValidCnpj('11.222.333/0001-81')).toBe(true);
    expect(isValidCnpj('11222333000181')).toBe(true);
  });
  it('accepts a valid alphanumeric CNPJ (RFB official example)', () => {
    expect(isValidCnpj('12.ABC.345/01DE-35')).toBe(true);
    expect(isValidCnpj('12ABC34501DE35')).toBe(true);
    expect(isValidCnpj('12abc34501de35')).toBe(true); // lowercase accepted
  });
  it('rejects invalid CNPJs', () => {
    expect(isValidCnpj('11.111.111/1111-11')).toBe(false);
    expect(isValidCnpj('11222333000100')).toBe(false);
    expect(isValidCnpj('12ABC34501DE34')).toBe(false); // wrong check digit
    expect(isValidCnpj('12ABC34501DE3X')).toBe(false); // non-numeric check digit
    expect(isValidCnpj('')).toBe(false);
  });
});
