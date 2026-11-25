import { Button, Input, Select, Typography } from "antd";
import { useEffect, useState } from "react";
import { recordToRows, rowsToRecord, type KeyValueRow } from "./config-editor-model";

export interface KeyValueEditorProps {
  value?: Record<string, string>;
  onChange?: (next: Record<string, string>) => void;
  valueOptions?: readonly string[];
  keyTitle?: string;
  valueTitle?: string;
  testId?: string;
}

export function KeyValueEditor({
  value, onChange, valueOptions, keyTitle = "目标字段", valueTitle = "来源字段", testId,
}: KeyValueEditorProps) {
  const incoming = JSON.stringify(value ?? {});
  const [rows, setRows] = useState<KeyValueRow[]>(() => recordToRows(value));
  const [lastIncoming, setLastIncoming] = useState(incoming);
  useEffect(() => {
    if (incoming === lastIncoming) return;
    setRows(recordToRows(value));
    setLastIncoming(incoming);
  }, [incoming, lastIncoming, value]);

  const emit = (next: KeyValueRow[]) => {
    setRows(next);
    onChange?.(rowsToRecord(next).record);
  };

  const { duplicateKeys } = rowsToRecord(rows);
  return (
    <div data-testid={testId} className="industrial-kv-editor">
      {rows.map((row, index) => (
        <div className="industrial-kv-row" key={index}>
          <Input
            aria-label={`${keyTitle} ${index + 1}`}
            value={row.key}
            placeholder={keyTitle}
            onChange={(event) => emit(rows.map((item, i) =>
              i === index ? { ...item, key: event.target.value } : item))}
          />
          {valueOptions && valueOptions.length > 0 ? (
            <Select
              aria-label={`${valueTitle} ${index + 1}`}
              value={row.value || undefined}
              placeholder={valueTitle}
              options={valueOptions.map((option) => ({ label: option, value: option }))}
              onChange={(selected) => emit(rows.map((item, i) =>
                i === index ? { ...item, value: selected ?? "" } : item))}
            />
          ) : (
            <Input
              aria-label={`${valueTitle} ${index + 1}`}
              value={row.value}
              placeholder={valueTitle}
              onChange={(event) => emit(rows.map((item, i) =>
                i === index ? { ...item, value: event.target.value } : item))}
            />
          )}
          <Button size="small" type="text" danger aria-label={`删除 ${index + 1}`}
            onClick={() => emit(rows.filter((_, i) => i !== index))}>删</Button>
        </div>
      ))}
      <Button size="small" type="dashed" block
        onClick={() => emit([...rows, { key: "", value: "" }])}>添加字段</Button>
      {duplicateKeys.length > 0 && (
        <Typography.Text type="danger" className="industrial-editor-error">
          重复字段：{duplicateKeys.join("、")}
        </Typography.Text>
      )}
    </div>
  );
}
