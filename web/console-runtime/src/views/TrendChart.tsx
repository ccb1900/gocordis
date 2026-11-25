// Lightweight SVG bar trend (no chart dependency): per-key totals with an
// overlaid warning series.
export interface TrendPoint {
  label: string;
  primary: number;
  warning: number;
}

export function TrendChart({ points, primaryLabel, warningLabel }: {
  points: TrendPoint[];
  primaryLabel: string;
  warningLabel: string;
}) {
  if (points.length === 0) return null;
  const max = Math.max(...points.map((p) => p.primary), 1);
  const w = 28;
  const gap = 14;
  const height = 120;
  return (
    <div style={{ overflowX: "auto" }}>
      <svg
        role="img"
        aria-label={`${primaryLabel}趋势`}
        width={points.length * (w + gap) + gap}
        height={height + 24}
        style={{ display: "block" }}
      >
        {points.map((p, i) => {
          const x = gap + i * (w + gap);
          const h = Math.round((p.primary / max) * (height - 20));
          const warnH = p.primary > 0 ? Math.round((p.warning / p.primary) * h) : 0;
          return (
            <g key={p.label}>
              <rect x={x} y={height - h} width={w} height={h} rx={3} fill={i % 2 ? "#3b4b6b" : "#4d6bfe"} opacity={0.85}>
                <title>{`${p.label}：${primaryLabel} ${p.primary}，${warningLabel} ${p.warning}`}</title>
              </rect>
              {p.warning > 0 && (
                <rect x={x} y={height - warnH} width={w} height={warnH} rx={3} fill="#f0655a" opacity={0.9}>
                  <title>{`${p.label}：${warningLabel} ${p.warning}`}</title>
                </rect>
              )}
              <text x={x + w / 2} y={height + 16} textAnchor="middle" fontSize={10} fill="currentColor" opacity={0.6}>
                {p.label}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
