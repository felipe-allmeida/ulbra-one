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
  readonly detail?: string;
  readonly errors?: FieldErrors;
  readonly traceId?: string;

  constructor(init: { status: number; title?: string; detail?: string; errors?: FieldErrors; traceId?: string }) {
    super(init.detail || init.title || `${init.status}`);
    this.name = 'ApiError';
    this.status = init.status;
    this.title = init.title;
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
  errors?: FieldErrors;
}

/** Builds an {@link ApiError} from a parsed problem+json body and the response status. */
export function apiErrorFromProblem(status: number, body: ProblemDetails): ApiError {
  return new ApiError({
    status,
    title: body.title,
    detail: body.detail,
    errors: body.errors,
    // ASP.NET puts the trace id either as `traceId` or in `instance`.
    traceId: body.traceId ?? body.instance,
  });
}

/** Flattens a validation error dictionary into a single readable line. */
function flattenFieldErrors(errors: FieldErrors): string {
  return Object.values(errors).flat().filter(Boolean).join(' ');
}

/**
 * Resolves any thrown value into a user-facing message.
 * For an {@link ApiError}: prefers `detail`, then joined validation `errors`, then `title`.
 * For a plain `Error`: its message. Otherwise a generic i18n fallback.
 */
export function getErrorMessage(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.detail) return e.detail;
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
