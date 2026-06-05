import { describe, it, expect } from 'vitest';
import { ApiError, apiErrorFromProblem, getErrorMessage } from './apiError';

describe('apiErrorFromProblem', () => {
  it('captures standard problem members and traceId', () => {
    const err = apiErrorFromProblem(404, {
      title: 'Not Found',
      status: 404,
      detail: 'Fornecedor não encontrado.',
      traceId: 'abc-123',
    });
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(404);
    expect(err.title).toBe('Not Found');
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
