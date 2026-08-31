// Base API client. The server's base URL is read from Vite env config, not
// hard-coded (TM-01 also requires no hard-coded target-environment URLs —
// this is the platform's own API, not a test target, but the same
// discipline applies).
export const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5174";

export interface HealthResponse {
  status: string;
  timestampUtc: string;
}

export async function getHealth(): Promise<HealthResponse> {
  const res = await fetch(`${API_BASE_URL}/api/health`);
  if (!res.ok) {
    throw new Error(`Health check failed: ${res.status}`);
  }
  return res.json();
}

/** The { title, status, detail } shape written by the server's exception handler. */
interface ProblemDetails {
  title?: string;
  status?: number;
  detail?: string;
}

/** Thrown by {@link apiRequest} for any non-2xx response, carrying the server's own message. */
export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

/**
 * Shared fetch wrapper for every typed API module (environments.ts and
 * later ones) — keeps error handling and JSON parsing in one place instead
 * of scattered raw fetch() calls through components.
 */
export async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    headers: {
      ...(init?.body ? { "Content-Type": "application/json" } : {}),
      ...init?.headers,
    },
  });

  if (!res.ok) {
    let detail = res.statusText;
    try {
      const problem = (await res.json()) as ProblemDetails;
      detail = problem.detail ?? problem.title ?? detail;
    } catch {
      // response wasn't JSON — fall back to statusText above
    }
    throw new ApiError(res.status, detail);
  }

  if (res.status === 204) {
    return undefined as T;
  }

  return res.json();
}
