import { useEffect, useRef, useState } from "react";
import * as signalR from "@microsoft/signalr";
import { API_BASE_URL } from "../api/client";
import type { RunSummary, ScenarioResult } from "../types/run";

export type ConnectionState = "connecting" | "live" | "reconnecting" | "disconnected";

interface UseRunLiveUpdatesOptions {
  runId: number;
  /** Incremental run header/status/totals update. */
  onRunUpdated: (run: RunSummary) => void;
  /** Incremental single-scenario upsert. */
  onScenarioUpdated: (scenario: ScenarioResult) => void;
  /** Called once the connection is (re)established, to re-fetch authoritative REST state (5.10/5.11). */
  onNeedsRefetch: () => void;
}

/**
 * TM-03 (Step 5) — the one reusable SignalR client for Run Details (5.7).
 * REST remains the source of initial truth; this hook only ever supplies
 * *subsequent* incremental changes (5.8). The hub URL is derived from the
 * same VITE_API_BASE_URL configuration as the REST client — never
 * hard-coded (5.7).
 */
export function useRunLiveUpdates({ runId, onRunUpdated, onScenarioUpdated, onNeedsRefetch }: UseRunLiveUpdatesOptions) {
  const [connectionState, setConnectionState] = useState<ConnectionState>("connecting");

  // Refs so the effect below doesn't need to restart when a parent re-render
  // hands it new (but behaviorally identical) callback closures.
  const onRunUpdatedRef = useRef(onRunUpdated);
  const onScenarioUpdatedRef = useRef(onScenarioUpdated);
  const onNeedsRefetchRef = useRef(onNeedsRefetch);
  onRunUpdatedRef.current = onRunUpdated;
  onScenarioUpdatedRef.current = onScenarioUpdated;
  onNeedsRefetchRef.current = onNeedsRefetch;

  useEffect(() => {
    if (!Number.isFinite(runId)) {
      return;
    }

    let disposed = false;
    setConnectionState("connecting");

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL}/hubs/runs`)
      .withAutomaticReconnect() // 5.11 — automatic reconnect, no polling fallback
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connection.on("RunUpdated", (run: RunSummary) => onRunUpdatedRef.current(run));
    connection.on("ScenarioUpdated", (scenario: ScenarioResult) => onScenarioUpdatedRef.current(scenario));

    connection.onreconnecting(() => {
      if (!disposed) setConnectionState("reconnecting");
    });

    connection.onreconnected(async () => {
      // 5.11 — after reconnecting: re-subscribe to the run group, then
      // re-fetch authoritative REST state so nothing missed while
      // disconnected is left stale.
      try {
        await connection.invoke("SubscribeToRun", runId);
      } finally {
        if (!disposed) {
          setConnectionState("live");
          onNeedsRefetchRef.current();
        }
      }
    });

    connection.onclose(() => {
      if (!disposed) setConnectionState("disconnected");
    });

    (async () => {
      try {
        // 5.10 — connect and subscribe first, then let the caller (re)fetch
        // REST state, so a fast transition during setup is never missed:
        // the live subscription is already active before the authoritative
        // snapshot is read.
        await connection.start();
        await connection.invoke("SubscribeToRun", runId);
        if (!disposed) {
          setConnectionState("live");
          onNeedsRefetchRef.current();
        }
      } catch {
        if (!disposed) setConnectionState("disconnected");
      }
    })();

    return () => {
      disposed = true;
      connection.stop();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [runId]);

  return { connectionState };
}
