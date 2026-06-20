export interface AppConfig {
  jobApiBaseUrl?: string;
  useMockJob: boolean;
}

export function readConfig(): AppConfig {
  return {
    jobApiBaseUrl: import.meta.env.VITE_JOB_API_BASE_URL,
    useMockJob: import.meta.env.VITE_USE_MOCK_JOB === "true",
  };
}
