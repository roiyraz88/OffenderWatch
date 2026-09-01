import { LoadingSpinner } from "./LoadingSpinner";

interface PageLoadingProps {
  label?: string;
}

/** Centered page/section-level loading state for an initial API fetch — never shown once real content is already on screen. */
export function PageLoading({ label = "Loading…" }: PageLoadingProps) {
  return (
    <div className="page-loading" role="status" aria-live="polite">
      <LoadingSpinner size="lg" announce={false} />
      <span>{label}</span>
    </div>
  );
}
