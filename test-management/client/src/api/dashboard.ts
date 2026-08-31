import { apiRequest } from "./client";
import type { Dashboard } from "../types/dashboard";

export function getDashboard(): Promise<Dashboard> {
  return apiRequest<Dashboard>("/api/dashboard");
}
