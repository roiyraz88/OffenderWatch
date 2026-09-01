interface LoadingSpinnerProps {
  /** "sm" fits inline in a button; "md" for small inline/section use; "lg" for a page-level spinner. */
  size?: "sm" | "md" | "lg";
  /** Accessible label. Ignored when announce=false (e.g. next to visible button text that already says "Starting…"). */
  label?: string;
  announce?: boolean;
}

/** The one spinner used everywhere in the app — CSS animation only, no library. */
export function LoadingSpinner({ size = "md", label = "Loading", announce = true }: LoadingSpinnerProps) {
  return (
    <span
      className={`spinner spinner-${size}`}
      role={announce ? "status" : undefined}
      aria-label={announce ? label : undefined}
      aria-hidden={announce ? undefined : true}
    />
  );
}
