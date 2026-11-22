// Detail view for GET /api/plugins/{id}/dependencies. Shows the two
// independent dependency axes from the paper: package version constraints
// (with installed version / satisfaction) and capability require/provide
// edges, plus host diagnostics. Re-queries on the `plugin` domain, so an SSE
// `plugin-state` frame refreshes it without any local wiring.

import React, { useEffect, useState } from "react";
import { Alert, Button, Empty, Skeleton, Space, Table, Tag, Typography } from "antd";
import type { TableColumnsType } from "antd";
import { ReloadOutlined } from "@ant-design/icons";
import { api } from "../api";
import { PLUGIN, useDomainVersion } from "../lib/projections";
import { diagnosticText } from "../lib/plugin-control";
import type { PackageDependencyView, PluginDependencyView } from "../types";

const errText = (e: unknown) => (e instanceof Error ? e.message : String(e));

function keyTags(values: string[], color: string) {
  return values.length ? (
    <Space size={4} wrap>
      {values.map((v) => <Tag key={v} color={color}>{v}</Tag>)}
    </Space>
  ) : (
    <Typography.Text type="secondary">—</Typography.Text>
  );
}

const depColumns: TableColumnsType<PackageDependencyView> = [
  { title: "包", dataIndex: "id", key: "id", width: 150, ellipsis: true,
    render: (v: string) => <Typography.Text code style={{ fontSize: 12 }}>{v}</Typography.Text> },
  { title: "约束", dataIndex: "versionRange", key: "versionRange", width: 130,
    render: (v: string) => v || "—" },
  { title: "已安装", dataIndex: "installedVersion", key: "installedVersion", width: 130,
    render: (v: string) => v || "—" },
  { title: "满足", dataIndex: "satisfied", key: "satisfied", width: 80,
    render: (v: boolean) => <Tag color={v ? "success" : "error"}>{v ? "是" : "否"}</Tag> },
];

export function PluginDependencies({ pluginId }: { pluginId: string }) {
  const [view, setView] = useState<PluginDependencyView | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const version = useDomainVersion(PLUGIN);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    api
      .dependencies(pluginId)
      .then((data) => {
        if (alive) {
          setView(data);
          setError(null);
        }
      })
      .catch((e) => {
        if (alive) {
          setView(null);
          setError(errText(e));
        }
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [pluginId, version, reload]);

  return (
    <div style={{ marginTop: 14, borderTop: "1px solid var(--line-soft, #232a3a)", paddingTop: 10 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 8 }}>
        <Typography.Text strong style={{ fontSize: 13 }}>依赖关系</Typography.Text>
        <Button
          size="small"
          type="text"
          icon={<ReloadOutlined />}
          loading={loading}
          onClick={() => setReload((n) => n + 1)}
        >
          刷新
        </Button>
      </div>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 8 }} />}
      {loading && !view ? (
        <Skeleton active paragraph={{ rows: 3 }} />
      ) : view ? (
        <Space direction="vertical" size={10} style={{ width: "100%" }}>
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>包依赖</Typography.Text>
            {view.packageDependencies.length ? (
              <Table
                size="small"
                rowKey={(r, i) => `${r.id}:${i}`}
                dataSource={view.packageDependencies}
                columns={depColumns}
                pagination={false}
              />
            ) : (
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="无包依赖" />
            )}
          </div>
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>被依赖（dependents）</Typography.Text>
            <div>{keyTags(view.dependents, "gold")}</div>
          </div>
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>能力依赖（requires）</Typography.Text>
            <div>{keyTags(view.capabilityRequires, "purple")}</div>
          </div>
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>提供能力（provides）</Typography.Text>
            <div>{keyTags(view.capabilityProvides, "blue")}</div>
          </div>
          {view.diagnostics.length > 0 && (
            <Alert
              type="warning"
              showIcon
              message={`诊断（${view.diagnostics.length}）`}
              description={
                <ul style={{ margin: 0, paddingLeft: 18 }}>
                  {view.diagnostics.map((d, i) => (
                    <li key={i} style={{ fontSize: 12 }}>{diagnosticText(d)}</li>
                  ))}
                </ul>
              }
            />
          )}
        </Space>
      ) : null}
    </div>
  );
}
