// Declarative page: title + description + actions + an ordered stack of
// view blocks. The page itself is only data, fed by hub queries/commands.
import React, { useState } from "react";
import { Button, DatePicker, Space } from "antd";
import dayjs from "dayjs";
import { ViewBlockRenderer, type ViewBlock, type ViewContext } from "./blocks";
import type { PageAction } from "./schema";

export function GenericPage({ title, description, views, actions = [], ctx }: {
  title: string;
  description?: string;
  views: ViewBlock[];
  actions?: PageAction[];
  ctx: ViewContext;
}) {
  const [dates, setDates] = useState<Record<string, string>>({});

  return (
    <>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 16, flexWrap: "wrap", gap: 12 }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 22 }}>{title}</h1>
          {description && <p style={{ color: "#8a93a6", marginBottom: 0 }}>{description}</p>}
        </div>
        <Space wrap>
          {actions.map((a) =>
            a.datePicker ? (
              <DatePicker
                key={`${a.label}-date`}
                value={dates[a.label] ? dayjs(dates[a.label]) : null}
                onChange={(d) => setDates((m) => ({ ...m, [a.label]: d ? d.format("YYYY-MM-DD") : "" }))}
                placeholder="选择日期（默认按调度策略）"
                style={{ width: 190 }}
              />
            ) : null
          )}
          {actions.map((a) => (
            <Button
              key={a.label}
              type="primary"
              loading={ctx.busy}
              onClick={() => void ctx.hubCommand(a.command, { date: dates[a.label] || undefined, reason: a.label })}
            >
              {a.label}
            </Button>
          ))}
        </Space>
      </div>
      {views.map((v, i) => (
        <div key={i} style={{ marginBottom: 18 }}>
          <ViewBlockRenderer block={v} ctx={ctx} />
        </div>
      ))}
    </>
  );
}
