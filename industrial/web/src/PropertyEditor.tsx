// Right property panel: schema-driven Ant Design Form for ordinary nodes,
// with a JSON fallback for complex schemas (docs/collector.md §7.5). Edges are
// listed with editable ports so a type mismatch can always be corrected.

import {
  Alert, Button, Divider, Empty, Form, Input, InputNumber, Select, Space, Switch, Table, Tag, Typography,
  type TableColumnsType,
} from "antd";
import { useEffect, useState, type ReactNode } from "react";
import type {
  JsonSchemaLike, NodeDescriptor, ResourceOption, ValidationIssue, WorkflowEdge, WorkflowNode,
} from "./types";
import { mergeAppliedConfig } from "./workflow-model";
import { editorKindForSchema, listSuggestions, valueOptionsOf } from "./config-editor-model";
import { KeyValueEditor } from "./KeyValueEditor";
import { StringListEditor } from "./StringListEditor";

export interface PropertyEditorProps {
  node: WorkflowNode | null;
  descriptor: NodeDescriptor | null;
  nodes: readonly WorkflowNode[];
  edges: WorkflowEdge[];
  catalog: ReadonlyMap<string, NodeDescriptor>;
  resources: readonly ResourceOption[];
  issues: ValidationIssue[];
  onApplyConfig: (config: Record<string, unknown>) => void;
  onDeleteNode: () => void;
  onDeleteEdge: (index: number) => void;
  onCreateEdge: (from: string, to: string) => void;
  onUpdateEdge: (index: number, edge: WorkflowEdge) => void;
  onSelectNode: (id: string) => void;
  onCreateResource?: () => void;
  selectedEdgeId?: string | null;
  sampleLines?: string[];
}

function JsonValueField({ value, onChange }: {
  value?: unknown; onChange?: (next: unknown) => void;
}) {
  const serialized = value === undefined ? "{}" : JSON.stringify(value, null, 2);
  const [text, setText] = useState(serialized);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setText(serialized); setError(null); }, [serialized]);
  const commit = (next: string) => {
    setText(next);
    try {
      const parsed: unknown = JSON.parse(next);
      setError(null);
      onChange?.(parsed);
    } catch (caught) {
      setError((caught as Error).message);
    }
  };
  return (
    <div>
      <Input.TextArea rows={5} value={text} onChange={(event) => commit(event.target.value)} />
      {error && <Typography.Text type="danger" style={{ fontSize: 11 }}>{error}</Typography.Text>}
    </div>
  );
}

function ResourcePicker({ value, onChange, resources, resourceType, label, onCreate }: {
  value?: string; onChange?: (next: string | undefined) => void;
  resources: readonly ResourceOption[]; resourceType?: string; label?: string;
  onCreate?: () => void;
}) {
  const options = resources
    .filter((resource) => resource.enabled !== false &&
      (!resourceType || resource.type === resourceType))
    .map((resource) => ({
      value: resource.id,
      label: `${resource.name} (${resource.type})`,
    }));
  const selected = resources.find((resource) => resource.id === value);
  return (
    <div>
      <select aria-label={label} value={value ?? ""}
        onChange={(event) => onChange?.(event.target.value || undefined)}>
        <option value="">未选择</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>{option.label}</option>
        ))}
      </select>
      {selected && (
        <Typography.Text type={selected.enabled === false ? "danger" : "success"}
          data-testid="resource-picker-status" style={{ fontSize: 11, display: "block" }}>
          {selected.name} · {selected.type} · {selected.enabled === false ? "已停用" : "已启用"}
          {selected.credentialId ? ` · 凭证 ${selected.credentialName ?? selected.credentialId}` : ""}
        </Typography.Text>
      )}
      <Button type="link" size="small" data-testid="resource-picker-create" onClick={onCreate}>
        新建资源
      </Button>
    </div>
  );
}

function SchemaField({ name, schema, required, resources, resourceType, properties, config, onCreateResource }: {
  name: string; schema: JsonSchemaLike; required: boolean;
  resources: readonly ResourceOption[]; resourceType?: string;
  properties: Record<string, JsonSchemaLike>; config: Record<string, unknown>;
  onCreateResource?: () => void;
}) {
  let control: ReactNode;
  let valuePropName: "value" | "checked" = "value";
  switch (editorKindForSchema(schema)) {
    case "resource":
      control = <ResourcePicker resources={resources} resourceType={resourceType}
        label={String(schema.title ?? name)} onCreate={onCreateResource} />;
      break;
    case "enum":
      control = (
        <Select allowClear
          options={(schema.enum ?? []).map((value) => ({ label: String(value), value }))} />
      );
      break;
    case "number":
      control = <InputNumber style={{ width: "100%" }} />;
      break;
    case "boolean":
      control = <Switch />;
      valuePropName = "checked";
      break;
    case "key-value":
      control = (
        <KeyValueEditor
          testId={`field-${name}`}
          valueOptions={valueOptionsOf(schema) ?? undefined}
          keyTitle={name === "mapping" ? "目标字段" : "字段名"}
          valueTitle={name === "mapping" ? "来源字段" : "类型"}
        />
      );
      break;
    case "string-list":
      control = (
        <StringListEditor testId={`field-${name}`} suggestions={listSuggestions(properties, config)} />
      );
      break;
    case "json":
      control = <JsonValueField />;
      break;
    default:
      control = <Input placeholder={schema.description} />;
      break;
  }
  return (
    <Form.Item
      key={name} name={name} label={schema.title ?? name} tooltip={schema.description}
      extra={(() => {
        const examples = (schema as { examples?: unknown[] }).examples;
        const hints = [
          schema.description,
          schema.default === undefined ? null : `默认：${JSON.stringify(schema.default)}`,
          Array.isArray(examples) && examples.length > 0
            ? `示例：${examples.map((item) => String(item)).join("、")}` : null,
        ].filter((hint): hint is string => typeof hint === "string" && hint.length > 0);
        return hints.length > 0
          ? <span data-testid={`field-hint-${name}`}>{hints.join(" · ")}</span>
          : undefined;
      })()}
      valuePropName={valuePropName}
      rules={required && valuePropName === "value"
        ? [{ required: true, message: `请填写 ${schema.title ?? name}` }]
        : []}
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
    node, descriptor, nodes, edges, catalog, resources, issues, onApplyConfig, onDeleteNode,
    onDeleteEdge, onCreateEdge, onUpdateEdge, onSelectNode, onCreateResource, selectedEdgeId, sampleLines,
  } = props;
  const [form] = Form.useForm<Record<string, unknown>>();
  const [json, setJson] = useState("{}");
  const [jsonError, setJsonError] = useState<string | null>(null);
  const [fromNode, setFromNode] = useState<string | undefined>();
  const [toNode, setToNode] = useState<string | undefined>();
  const [showAllEdges, setShowAllEdges] = useState(false);
  const [advanced, setAdvanced] = useState(false);

  useEffect(() => {
    form.setFieldsValue(node?.config ?? {});
    setJson(JSON.stringify(node?.config ?? {}, null, 2));
    setJsonError(null);
  }, [form, node?.id, node?.config]);

  const requiredFields = new Set(descriptor?.configurationSchema.required ?? []);
  const fields = Object.entries(descriptor?.configurationSchema.properties ?? {});

  const apply = () => {
    if (fields.length > 0) {
      void form.validateFields().then((values) =>
        onApplyConfig(mergeAppliedConfig(node?.config ?? {}, values)));
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

  const visibleEdges = node && !showAllEdges
    ? edges.map((edge, index) => ({ edge, index }))
        .filter(({ edge }) => edge.source === node.id || edge.target === node.id)
    : edges.map((edge, index) => ({ edge, index }));
  const edgeRows: EdgeRow[] = visibleEdges.map(({ edge, index }) => ({
    ...edge, index, key: `${index}:${edge.source}->${edge.target}`,
  }));
  const edgeColumns: TableColumnsType<EdgeRow> = [
    {
      title: "起点节点", key: "reconnect-source", width: 108,
      render: (_: unknown, row: EdgeRow) => (
        <select aria-label={`连线 ${row.index} 起点节点`} value={row.source}
          onChange={(event) => onUpdateEdge(row.index, { ...row, source: event.target.value })}>
          {nodes.map((item) => <option key={item.id} value={item.id}>{item.id}</option>)}
        </select>
      ),
    },
    {
      title: "终点节点", key: "reconnect-target", width: 108,
      render: (_: unknown, row: EdgeRow) => (
        <select aria-label={`连线 ${row.index} 终点节点`} value={row.target}
          onChange={(event) => onUpdateEdge(row.index, { ...row, target: event.target.value })}>
          {nodes.map((item) => <option key={item.id} value={item.id}>{item.id}</option>)}
        </select>
      ),
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
    <div className="industrial-properties-body">
      {!node && <Empty description="在画布中选中节点以编辑配置" style={{ marginTop: 24 }} />}
      {node && (
        <>
          <Typography.Title level={5} style={{ margin: 0 }}>{descriptor?.name ?? node.type}</Typography.Title>
          <Typography.Text type="secondary" style={{ fontSize: 11 }}>{node.id}</Typography.Text>
          <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 6 }}>
            {descriptor?.description ?? "请按业务含义配置该节点。"}
          </Typography.Paragraph>
          <Space wrap size={4} style={{ marginBottom: 8 }}>
            <Tag color="blue">{descriptor?.category ?? "采集节点"}</Tag>
            <Tag>版本 {descriptor?.version ?? "1.0.0"}</Tag>
            {descriptor?.inputs.map((port) => (
              <Tag key={`in-${port.name}`} color={port.required ? "red" : "default"}>
                输入 {port.name} · {port.type}
              </Tag>
            ))}
            {descriptor?.outputs.map((port) => (
              <Tag key={`out-${port.name}`} color="green">输出 {port.name} · {port.type}</Tag>
            ))}
          </Space>
          {(node.type.includes("reader") || node.type.includes("mapping") ||
            node.type.includes("source") || node.type.includes("filesystem")) && sampleLines && sampleLines.length > 0 && <>
            <Typography.Text strong style={{ fontSize: 12 }}>样本预览</Typography.Text>
            <pre className="industrial-sample-preview">{sampleLines.join("\n")}</pre>
          </>}
          <Form form={form} layout="vertical" size="small" style={{ marginTop: 12 }}>
            {fields.map(([name, schema]) => (
              <SchemaField
                key={name} name={name} schema={schema} required={requiredFields.has(name)}
                resources={resources}
                resourceType={typeof descriptor?.configurationSchema.resourceType === "string"
                  ? descriptor.configurationSchema.resourceType
                  : undefined}
                properties={descriptor?.configurationSchema.properties ?? {}}
                config={node?.config ?? {}}
                onCreateResource={onCreateResource}
              />
            ))}
          </Form>
          {fields.length === 0 && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              此节点无需填写业务参数；保留默认配置即可。
            </Typography.Text>
          )}
          <Button type="link" size="small" onClick={() => setAdvanced((value) => !value)}>
            {advanced ? "收起高级配置" : "高级配置"}
          </Button>
          {advanced && (
            <>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                高级配置直接编辑节点 JSON，仅在需要复杂表达式时使用。
              </Typography.Text>
              <Input.TextArea
                rows={8}
                value={json}
                style={{ marginTop: 6 }}
                onChange={(event) => setJson(event.target.value)}
              />
              {jsonError && <Typography.Text type="danger" style={{ fontSize: 12 }}>{jsonError}</Typography.Text>}
              {descriptor?.activityType && (
                <Typography.Text type="secondary" style={{ fontSize: 11, display: "block" }}>
                  运行时 Activity：{descriptor.activityType}
                </Typography.Text>
              )}
            </>
          )}
          <Button type="primary" block onClick={apply}>应用配置</Button>
          <Button danger block style={{ marginTop: 8 }} onClick={onDeleteNode}>删除节点</Button>
        </>
      )}
      {selectedEdgeId && (() => {
        const index = edges.findIndex((edge) =>
          `e:${edge.source}:${edge.sourcePort}>${edge.target}:${edge.targetPort}` === selectedEdgeId);
        if (index < 0) return null;
        const edge = edges[index];
        return <Alert type="info" showIcon style={{ marginTop: 8 }}
          message={`当前连线：${edge.source}.${edge.sourcePort} → ${edge.target}.${edge.targetPort}`}
          description={<Button size="small" danger onClick={() => onDeleteEdge(index)}>删除此连线</Button>} />;
      })()}
      <Divider plain style={{ margin: "16px 0 8px" }}>相关连线 ({edgeRows.length} / {edges.length})</Divider>
      {node && <Button type="link" size="small" onClick={() => setShowAllEdges((value) => !value)}>
        {showAllEdges ? "仅看当前节点" : "显示全部连线"}
      </Button>}
      {nodes.length > 1 && <Space wrap style={{ marginBottom: 8 }}>
        <select aria-label="连线起点" value={fromNode ?? ""}
          onChange={(event) => setFromNode(event.target.value || undefined)}>
          <option value="">起点节点</option>
          {nodes.map((item) => <option key={item.id} value={item.id}>{item.id}</option>)}
        </select>
        <select aria-label="连线终点" value={toNode ?? ""}
          onChange={(event) => setToNode(event.target.value || undefined)}>
          <option value="">终点节点</option>
          {nodes.map((item) => <option key={item.id} value={item.id}>{item.id}</option>)}
        </select>
        <Button size="small" disabled={!fromNode || !toNode || fromNode === toNode}
          onClick={() => {
            if (fromNode && toNode) onCreateEdge(fromNode, toNode);
          }}>
          添加连线
        </Button>
      </Space>}
      {edges.length === 0
        ? <Typography.Text type="secondary" style={{ fontSize: 12 }}>从节点端口拖到另一个节点即可连线；也可以在这里编辑端口。</Typography.Text>
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
    </div>
  );
}
