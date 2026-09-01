import { useEffect, useState } from "react";
import { getHealth } from "../api/client";

type HealthState = "checking" | "ok" | "error";

const POLL_INTERVAL_MS = 10_000;

/**
 * Tracks GET /api/health so the navbar badge reflects real backend
 * connectivity without a page refresh: re-checked every 10s, and again
 * immediately whenever the tab regains focus (catching the common "backend
 * was restarted/died while this tab was in the background" case faster
 * than the next poll tick would).
 */
export function useHealth(): HealthState {
  const [state, setState] = useState<HealthState>("checking");

  useEffect(() => {
    let cancelled = false;

    async function check() {
      try {
        await getHealth();
        if (!cancelled) setState("ok");
      } catch {
        if (!cancelled) setState("error");
      }
    }

    check();
    const intervalId = window.setInterval(check, POLL_INTERVAL_MS);

    function handleFocus() {
      check();
    }
    function handleVisibilityChange() {
      if (document.visibilityState === "visible") check();
    }
    window.addEventListener("focus", handleFocus);
    document.addEventListener("visibilitychange", handleVisibilityChange);

    return () => {
      cancelled = true;
      window.clearInterval(intervalId);
      window.removeEventListener("focus", handleFocus);
      document.removeEventListener("visibilitychange", handleVisibilityChange);
    };
  }, []);

  return state;
}
