import { describe, it, expect } from 'vitest';
import { ApiError, apiErrorFromProblem, getErrorMessage } from './apiError';

describe('apiErrorFromProblem', () => {
  it('captures standard problem members and traceId', () => {
    const err = apiErrorFromProblem(404, {
      title: 'Resource not found',
      status: 404,
      detail: 'Fornecedor não encontrado.',
      error_code: 'vendor.not_found',
      trace_id: 'abc-123',
    });
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(404);
    expect(err.title).toBe('Resource not found');
    expect(err.errorCode).toBe('vendor.not_found');
    expect(err.detail).toBe('Fornecedor não encontrado.');
    expect(err.traceId).toBe('abc-123');
  });

  it('falls back to instance for traceId and keeps validation errors', () => {
    const err = apiErrorFromProblem(400, {
      title: 'One or more validation errors occurred.',
      status: 400,
      instance: 'POST /api/vendors',
      errors: { Document: ['CNPJ inválido.'], Name: ['Obrigatório.'] },
    });
    expect(err.traceId).toBe('POST /api/vendors');
    expect(err.errors).toEqual({ Document: ['CNPJ inválido.'], Name: ['Obrigatório.'] });
  });
});

describe('getErrorMessage', () => {
  it('prefers detail on an ApiError', () => {
    const err = new ApiError({ status: 409, title: 'Conflict', detail: 'Documento já cadastrado.' });
    expect(getErrorMessage(err)).toBe('Documento já cadastrado.');
  });

  it('joins validation errors when there is no detail', () => {
    const err = new ApiError({
      status: 400,
      title: 'Validation failed',
      errors: { Document: ['CNPJ inválido.'], Name: ['Obrigatório.'] },
    });
    expect(getErrorMessage(err)).toBe('CNPJ inválido. Obrigatório.');
  });

  it('maps a known backend error code to pt-BR, ignoring the English title', () => {
    const err = new ApiError({
      status: 404,
      title: 'Resource not found',
      errorCode: 'company.not_found',
      detail: 'Company not found.',
    });
    expect(getErrorMessage(err)).toBe('Empresa não encontrada.');
  });

  it('explains the invitation-only sign-in denial (auth.user_not_registered)', () => {
    const err = new ApiError({
      status: 403,
      title: 'Forbidden',
      errorCode: 'auth.user_not_registered',
      detail: "Email 'x@y.com' is not registered.",
    });
    expect(getErrorMessage(err)).toBe(
      'Seu e-mail ainda não está cadastrado no ULBRA One. Peça a um administrador para cadastrá-lo.',
    );
  });

  it('falls back to detail when the error code has no translation', () => {
    const err = new ApiError({
      status: 400,
      title: 'Invalid request',
      errorCode: 'some.unknown_code',
      detail: 'Something specific went wrong.',
    });
    expect(getErrorMessage(err)).toBe('Something specific went wrong.');
  });

  it('prefers detail over the catalogue translation for remittance.nothing_to_remit (detail-preferred)', () => {
    // The backend returns a dynamic per-reason detail; it must win over the generic catalogue text.
    const err = new ApiError({
      status: 400,
      title: 'Invalid request',
      errorCode: 'remittance.nothing_to_remit',
      detail: '12 parcelas descartadas: 8× sem forma de pagamento.',
    });
    expect(getErrorMessage(err)).toBe('12 parcelas descartadas: 8× sem forma de pagamento.');
  });

  it('falls back to the catalogue translation for remittance.nothing_to_remit when there is no detail', () => {
    const err = new ApiError({ status: 400, title: 'Invalid request', errorCode: 'remittance.nothing_to_remit' });
    expect(getErrorMessage(err)).toBe('Nenhuma parcela elegível para remessa.');
  });

  it('falls back to title when no detail or errors', () => {
    const err = new ApiError({ status: 500, title: 'Internal Server Error' });
    expect(getErrorMessage(err)).toBe('Internal Server Error');
  });

  it('returns the message of a plain Error', () => {
    expect(getErrorMessage(new Error('boom'))).toBe('boom');
  });

  it('returns a generic fallback for unknown values', () => {
    expect(getErrorMessage(null)).toBe('Algo deu errado. Tente novamente em instantes.');
    expect(getErrorMessage(undefined)).toBe('Algo deu errado. Tente novamente em instantes.');
    expect(getErrorMessage({})).toBe('Algo deu errado. Tente novamente em instantes.');
  });

  it('returns a non-empty string', () => {
    expect(getErrorMessage(new ApiError({ status: 500 })).length).toBeGreaterThan(0);
  });
});
