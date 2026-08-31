import type { DashboardTrendPoint } from "../types/dashboard";

interface PassRateTrendChartProps {
  points: DashboardTrendPoint[];
}

const WIDTH = 640;
const HEIGHT = 180;
const PAD_LEFT = 34;
const PAD_RIGHT = 12;
const PAD_TOP = 12;
const PAD_BOTTOM = 24;

function formatTime(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

// TM-07 (8.12) — a lightweight, dependency-free SVG trend line. No charting
// library: this is the whole implementation.
export function PassRateTrendChart({ points }: PassRateTrendChartProps) {
  if (points.length === 0) {
    return <p className="trend-empty">No trend data yet — it appears once a Run has completed at least one scenario.</p>;
  }

  const plotWidth = WIDTH - PAD_LEFT - PAD_RIGHT;
  const plotHeight = HEIGHT - PAD_TOP - PAD_BOTTOM;

  const withRate = points.filter((p) => p.passRate !== null);
  const x = (i: number) => (points.length === 1 ? PAD_LEFT + plotWidth / 2 : PAD_LEFT + (i / (points.length - 1)) * plotWidth);
  const y = (rate: number) => PAD_TOP + plotHeight - (rate / 100) * plotHeight;

  const linePath = withRate
    .map((p) => `${x(points.indexOf(p))},${y(p.passRate as number)}`)
    .join(" ");

  return (
    <div className="trend-chart-wrap">
      <svg viewBox={`0 0 ${WIDTH} ${HEIGHT}`} className="trend-chart" role="img" aria-label="Pass rate trend">
        {[0, 25, 50, 75, 100].map((tick) => (
          <g key={tick}>
            <line x1={PAD_LEFT} x2={WIDTH - PAD_RIGHT} y1={y(tick)} y2={y(tick)} className="trend-gridline" />
            <text x={PAD_LEFT - 6} y={y(tick) + 3} className="trend-axis-label" textAnchor="end">
              {tick}%
            </text>
          </g>
        ))}

        {withRate.length > 1 && <polyline points={linePath} className="trend-line" fill="none" />}

        {points.map((p, i) => {
          if (p.passRate === null) return null;
          return (
            <g key={p.runId}>
              <circle cx={x(i)} cy={y(p.passRate)} r={4} className="trend-point">
                <title>
                  Run #{p.runId} · {p.environmentNameSnapshot} · {p.passRate.toFixed(1)}% · {p.passedCount} passed / {p.failedCount} failed /{" "}
                  {p.expectedFailedCount} expected-fail
                </title>
              </circle>
              {(i === 0 || i === points.length - 1 || points.length <= 6) && (
                <text x={x(i)} y={HEIGHT - 6} className="trend-axis-label" textAnchor="middle">
                  {formatTime(p.timestampUtc)}
                </text>
              )}
            </g>
          );
        })}
      </svg>
    </div>
  );
}
