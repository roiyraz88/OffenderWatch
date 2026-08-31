export interface Environment {
  id: number;
  name: string;
  baseUrl: string;
  isDefault: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CreateEnvironmentRequest {
  name: string;
  baseUrl: string;
  isDefault?: boolean;
}

export interface UpdateEnvironmentRequest {
  name: string;
  baseUrl: string;
}
