import { apiRequest } from "./client";
import type { CreateEnvironmentRequest, Environment, UpdateEnvironmentRequest } from "../types/environment";

const BASE_PATH = "/api/environments";

export function getEnvironments(): Promise<Environment[]> {
  return apiRequest<Environment[]>(BASE_PATH);
}

export function getEnvironment(id: number): Promise<Environment> {
  return apiRequest<Environment>(`${BASE_PATH}/${id}`);
}

export function createEnvironment(request: CreateEnvironmentRequest): Promise<Environment> {
  return apiRequest<Environment>(BASE_PATH, {
    method: "POST",
    body: JSON.stringify(request),
  });
}

export function updateEnvironment(id: number, request: UpdateEnvironmentRequest): Promise<Environment> {
  return apiRequest<Environment>(`${BASE_PATH}/${id}`, {
    method: "PUT",
    body: JSON.stringify(request),
  });
}

export function deleteEnvironment(id: number): Promise<void> {
  return apiRequest<void>(`${BASE_PATH}/${id}`, { method: "DELETE" });
}

export function setDefaultEnvironment(id: number): Promise<Environment> {
  return apiRequest<Environment>(`${BASE_PATH}/${id}/default`, { method: "PUT" });
}
