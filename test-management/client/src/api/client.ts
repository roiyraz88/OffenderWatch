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
