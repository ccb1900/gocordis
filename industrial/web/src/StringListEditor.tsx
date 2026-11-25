import { Button, Input, Typography } from "antd";
import { useEffect, useState } from "react";
import { normalizeStringList } from "./config-editor-model";

export interface StringListEditorProps {
  value?: string[];
  onChange?: (next: string[]) => void;
  suggestions?: readonly string[];
  testId?: string;
}

export function StringListEditor({ value, onChange, suggestions = [], testId }: StringListEditorProps) {
  const incoming = JSON.stringify(normalizeStringList(value));
  const [items, setItems] = useState<string[]>(() => normalizeStringList(value));
  const [lastIncoming, setLastIncoming] = useState(incoming);
  useEffect(() => {
    if (incoming === lastIncoming) return;
    setItems(normalizeStringList(value));
    setLastIncoming(incoming);
  }, [incoming, lastIncoming, value]);

  const emit = (next: string[]) => {
    // Keep blank rows visible while editing, but never persist them.
    setItems(next);
    onChange?.(normalizeStringList(next));
  };

  return (
    <div data-testid={testId} className="industrial-list-editor">
      <datalist id={`${testId ?? "list"}-suggestions`}>
        {suggestions.map((suggestion) => <option key={suggestion} value={suggestion} />)}
      </datalist>
      {items.map((item, index) => (
        <div className="industrial-list-row" key={index}>
          <Input
            aria-label={`字段 ${index + 1}`}
            value={item}
            list={`${testId ?? "list"}-suggestions`}
            onChange={(event) => emit(items.map((value, i) => i === index ? event.target.value : value))}
          />
          <Button size="small" type="text" danger aria-label={`删除 ${index + 1}`}
            onClick={() => emit(items.filter((_, i) => i !== index))}>删</Button>
        </div>
      ))}
      <Button size="small" type="dashed" block onClick={() => emit([...items, ""])}>添加字段</Button>
      {items.length === 0 && (
        <Typography.Text type="secondary" className="industrial-editor-error">
          尚未添加字段
        </Typography.Text>
      )}
    </div>
  );
}
