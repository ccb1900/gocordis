// Right property panel: schema-driven Ant Design Form for ordinary nodes,
// with a JSON fallback for complex schemas (docs/collector.md §7.5). Edges are
// listed with editable ports so a type mismatch can always be corrected.

import {
  Button, Divider, Empty, Form, Input, InputNumber, Select, Switch, Table, Tag, Typography,
  type TableColumnsType,
} from "antd";
import { useEffect, useState, type ReactNode } from "react";
import type { JsonSchemaLike, NodeDescriptor, ValidationIssue, WorkflowEdge, WorkflowNode } from "./types";

export interface PropertyEditorProps {
  node: WorkflowNode | null;
  descriptor: NodeDescriptor | null;
  edges: WorkflowEdge[];
  catalog: ReadonlyMap<string, NodeDescriptor>;
  issues: ValidationIssue[];
  onApplyConfig: (config: Record<string, unknown>) => void;
  onDeleteNode: () => void;
  onDeleteEdge: (index: number) => void;
  onUpdateEdge: (index: number, edge: WorkflowEdge) => void;
  onSelectNode: (id: string) => void;
}

function SchemaField({ name, schema, required }: {
  name: string; schema: JsonSchemaLike; required: boolean;
}) {
  let control: ReactNode;
  if (schema.enum && schema.enum.length > 0) {
    control = (
      <Select
        allowClear
        options={schema.enum.map((value) => ({ label: String(value), value }))}
      />
    );
  } else if (schema.type === "number" || schema.type === "integer") {
    control = <InputNumber style={{ width: "100%" }} />;
  } else if (schema.type === "boolean") {
    control = <Switch />;
  } else {
    control = <Input placeholder={schema.description} />;
  }
  return (
    <Form.Item
      key={name} name={name} label={schema.title ?? name} tooltip={schema.description}
      rules={required ? [{ required: true, message: `请填写 ${schema.title ?? name}` }] : []}
    >
      {control}
    </Form.Item>
  );
}

interface EdgeRow extends WorkflowEdge {
  index: number;
  key: string;
}

export function PropertyEditor(props: PropertyEditorProps) {
  const {
    node, descriptor, edges, catalog, issues, onApplyConfig, onDeleteNode,
    onDeleteEdge, onUpdateEdge, onSelectNode,
  } = props;
  const [form] = Form.useForm<Record<string, unknown>>();
  const [json, setJson] = useState("{}");
  const [jsonError, setJsonError] = useState<string | null>(null);

  useEffect(() => {
    form.setFieldsValue(node?.config ?? {});
    setJson(JSON.stringify(node?.config ?? {}, null, 2));
    setJsonError(null);
  }, [form, node?.id, node?.config]);

  const requiredFields = new Set(descriptor?.configurationSchema.required ?? []);
  const fields = Object.entries(descriptor?.configurationSchema.properties ?? {});

  const apply = () => {
    if (fields.length > 0) {
      void form.validateFields().then((values) => onApplyConfig(values));
      return;
    }
    try {
      const parsed: unknown = JSON.parse(json);
      if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
        throw new Error("配置必须是 JSON 对象");
      }
      setJsonError(null);
      onApplyConfig(parsed as Record<string, unknown>);
    } catch (error) {
      setJsonError((error as Error).message);
    }
  };

  const edgeRows: EdgeRow[] = edges.map((edge, index) => ({
    ...edge, index, key: `${index}:${edge.source}->${edge.target}`,
  }));
  const edgeColumns: TableColumnsType<EdgeRow> = [
    {
      title: "连线", dataIndex: "source", key: "source", width: 110,
      render: (_: unknown, row: EdgeRow) =>
        <Typography.Text style={{ fontSize: 11 }}>{row.source} → {row.target}</Typography.Text>,
    },
    {
      title: "端口", key: "ports",
      render: (_: unknown, row: EdgeRow) => {
        const from = catalog.get(row.source);
        const to = catalog.get(row.target);
        return (
          <div className="industrial-edge-ports">
            <Select
              size="small"
              value={row.sourcePort}
              style={{ width: 110 }}
              onChange={(sourcePort) => onUpdateEdge(row.index, { ...row, sourcePort })}
              options={(from?.outputs ?? []).map((port) => ({ label: `${port.name} (${port.type})`, value: port.name }))}
            />
            <Select
              size="small"
              value={row.targetPort}
              style={{ width: 110 }}
              onChange={(targetPort) => onUpdateEdge(row.index, { ...row, targetPort })}
              options={(to?.inputs ?? []).map((port) => ({ label: `${port.name} (${port.type})`, value: port.name }))}
            />
          </div>
        );
      },
    },
    {
      title: "", key: "actions", width: 44,
      render: (_: unknown, row: EdgeRow) =>
        <Button danger size="small" type="text" onClick={() => onDeleteEdge(row.index)}>删</Button>,
    },
  ];

  return (
    <aside className="industrial-properties">
      <Typography.Title level={5} style={{ marginTop: 0 }}>节点属性</Typography.Title>
      {!node && <Empty description="在画布中选中节点以编辑配置" style={{ marginTop: 24 }} />}
      {node && (
        <>
          <Typography.Text strong>{descriptor?.name ?? node.type}</Typography.Text>
          <Tag color="blue" style={{ marginLeft: 8 }}>{node.id}</Tag>
          <Form form={form} layout="vertical" size="small" style={{ marginTop: 12 }}>
            {fields.map(([name, schema]) => (
              <SchemaField key={name} name={name} schema={schema} required={requiredFields.has(name)} />
            ))}
          </Form>
          {fields.length === 0 && (
            <>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>配置 (JSON)</Typography.Text>
              <Input.TextArea
                rows={8}
                value={json}
                style={{ marginTop: 6 }}
                onChange={(event) => setJson(event.target.value)}
              />
              {jsonError && <Typography.Text type="danger" style={{ fontSize: 12 }}>{jsonError}</Typography.Text>}
            </>
          )}
          <Button type="primary" block onClick={apply}>应用配置</Button>
          <Button danger block style={{ marginTop: 8 }} onClick={onDeleteNode}>删除节点</Button>
          {descriptor?.activityType && (
            <Typography.Text type="secondary" style={{ fontSize: 11, display: "block", marginTop: 8 }}>
              {descriptor.activityType}
            </Typography.Text>
          )}
        </>
      )}
      <Divider plain style={{ margin: "16px 0 8px" }}>连线 ({edges.length})</Divider>
      {edges.length === 0
        ? <Typography.Text type="secondary" style={{ fontSize: 12 }}>切换到“连线”模式，从节点拖到另一个节点；也可以在这里编辑端口。</Typography.Text>
        : <Table size="small" columns={edgeColumns} dataSource={edgeRows} pagination={false} />}
      {issues.length > 0 && (
        <>
          <Divider plain style={{ margin: "16px 0 8px" }}>校验定位 ({issues.length})</Divider>
          {issues.map((item, index) => (
            <div key={`${item.path}-${index}`} className="industrial-issue">
              <Tag color="red">{item.code}</Tag>
              <Typography.Text
                style={{ fontSize: 12, cursor: "pointer" }}
                onClick={() => {
                  const match = /^nodes\.([^.]+)/.exec(item.path);
                  if (match) onSelectNode(match[1]);
                }}
              >
                {item.path}: {item.message}
              </Typography.Text>
            </div>
          ))}
        </>
      )}
    </aside>
  );
}
