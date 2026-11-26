// Right property panel: schema-driven Ant Design Form for ordinary nodes,
// with a JSON fallback for complex schemas (docs/collector.md §7.5). Edges are
// listed with editable ports so a type mismatch can always be corrected.

import {
  Button, Divider, Empty, Form, Input, InputNumber, Select, Space, Switch, Table, Tag, Typography,
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

function ResourcePicker({ value, onChange, resources, resourceType, label }: {
  value?: string; onChange?: (next: string | undefined) => void;
  resources: readonly ResourceOption[]; resourceType?: string; label?: string;
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
      {selected?.credentialId && (
        <Typography.Text type="secondary" style={{ fontSize: 11, display: "block" }}>
          凭证：{selected.credentialName ?? selected.credentialId}
        </Typography.Text>
      )}
    </div>
  );
}

function SchemaField({ name, schema, required, resources, resourceType, properties, config }: {
  name: string; schema: JsonSchemaLike; required: boolean;
  resources: readonly ResourceOption[]; resourceType?: string;
  properties: Record<string, JsonSchemaLike>; config: Record<string, unknown>;
}) {
  let control: ReactNode;
  let valuePropName: "value" | "checked" = "value";
  switch (editorKindForSchema(schema)) {
    case "resource":
      control = <ResourcePicker resources={resources} resourceType={resourceType}
        label={String(schema.title ?? name)} />;
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
    onDeleteEdge, onCreateEdge, onUpdateEdge, onSelectNode,
  } = props;
  const [form] = Form.useForm<Record<string, unknown>>();
  const [json, setJson] = useState("{}");
  const [jsonError, setJsonError] = useState<string | null>(null);
  const [fromNode, setFromNode] = useState<string | undefined>();
  const [toNode, setToNode] = useState<string | undefined>();

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
    <div className="industrial-properties-body">
      {!node && <Empty description="在画布中选中节点以编辑配置" style={{ marginTop: 24 }} />}
      {node && (
        <>
          <Typography.Text strong>{descriptor?.name ?? node.type}</Typography.Text>
          <Tag color="blue" style={{ marginLeft: 8 }}>{node.id}</Tag>
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
              />
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
