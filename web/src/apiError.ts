import i18n from './i18n';

/** Field name → list of validation messages (FluentValidation / ValidationProblem shape). */
export type FieldErrors = Record<string, string[]>;

/**
 * Typed error for a non-2xx API response that carried RFC7807 Problem Details
 * (`application/problem+json`). Captures the standard members plus the optional
 * `errors` validation dictionary and a `traceId` for support correlation.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly title?: string;
  /** Stable machine value from the backend catalogue (`error_code`), e.g. `payment.overpay`. */
  readonly errorCode?: string;
  readonly detail?: string;
  readonly errors?: FieldErrors;
  readonly traceId?: string;

  constructor(init: {
    status: number;
    title?: string;
    errorCode?: string;
    detail?: string;
    errors?: FieldErrors;
    traceId?: string;
  }) {
    super(init.detail || init.title || `${init.status}`);
    this.name = 'ApiError';
    this.status = init.status;
    this.title = init.title;
    this.errorCode = init.errorCode;
    this.detail = init.detail;
    this.errors = init.errors;
    this.traceId = init.traceId;
  }
}

interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  trace_id?: string;
  error_code?: string;
  errors?: FieldErrors;
}

/** Builds an {@link ApiError} from a parsed problem+json body and the response status. */
export function apiErrorFromProblem(status: number, body: ProblemDetails): ApiError {
  return new ApiError({
    status,
    title: body.title,
    errorCode: body.error_code,
    detail: body.detail,
    errors: body.errors,
    // Every error from ErrorResults carries `trace_id`; the other two are the pre-envelope fallbacks.
    traceId: body.trace_id ?? body.traceId ?? body.instance,
  });
}

/** Flattens a validation error dictionary into a single readable line. */
function flattenFieldErrors(errors: FieldErrors): string {
  return Object.values(errors).flat().filter(Boolean).join(' ');
}

/**
 * Backend error codes whose `detail` carries dynamic, case-specific text that must win over the static
 * `errors.codes` catalogue translation. Kept tiny and explicit so the global precedence (translation
 * first) stays intact for every other code.
 */
const DETAIL_PREFERRED_CODES = new Set(['remittance.nothing_to_remit']);

/**
 * Resolves any thrown value into a user-facing message.
 * For an {@link ApiError}: if `errorCode` is in the pt-BR catalogue, returns that text — this is the
 * only translated branch, which is why the backend sends a stable code rather than a phrase. Otherwise
 * falls back to `detail` (English, from the API), then joined validation `errors`, then `title`. For a
 * plain `Error`: its message. Otherwise a generic i18n fallback.
 * Exception: for {@link DETAIL_PREFERRED_CODES}, `detail` wins over the catalogue text (the translation
 * is only the fallback when `detail` is absent).
 */
export function getErrorMessage(e: unknown): string {
  if (e instanceof ApiError) {
    const key = e.errorCode ? 'errors.codes.' + e.errorCode : null;
    const mapped = key && i18n.exists(key) ? i18n.t(key) : null;
    if (mapped && !(e.errorCode && DETAIL_PREFERRED_CODES.has(e.errorCode))) return mapped;
    if (e.detail) return e.detail;
    if (mapped) return mapped;
    if (e.errors && Object.keys(e.errors).length > 0) {
      const flat = flattenFieldErrors(e.errors);
      if (flat) return flat;
    }
    if (e.title) return e.title;
    return i18n.t('errors.generic');
  }
  if (e instanceof Error && e.message) return e.message;
  if (typeof e === 'string' && e) return e;
  return i18n.t('errors.generic');
}
