// Reusable "pick a package file → upload → hand the uploadKey to a mutation"
// control. Install and Upgrade share this flow so neither invents its own
// multipart handling. The actual install/upgrade command is owned by the
// caller (Explorer), which also interprets PluginMutationResult.

import React, { useRef, useState } from "react";
import { Button, Typography } from "antd";
import { UploadOutlined } from "@ant-design/icons";
import { api } from "../api";
import type { UploadedPackage } from "../types";

const errText = (e: unknown) => (e instanceof Error ? e.message : String(e));

const DEFAULT_ACCEPT = ".zip,.tar,.tgz,.wasm,.dll,.nupkg,.json";

export function PluginFileButton({
  label,
  onUploaded,
  disabled,
  danger,
  accept = DEFAULT_ACCEPT,
  type = "primary",
}: {
  label: string;
  onUploaded: (pkg: UploadedPackage) => void | Promise<void>;
  disabled?: boolean;
  danger?: boolean;
  accept?: string;
  type?: "primary" | "default";
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const pick = () => {
    setError(null);
    inputRef.current?.click();
  };

  const change = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    // Reset so selecting the same file twice still fires `change`.
    e.target.value = "";
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const pkg = await api.uploadPackage(file);
      if (!pkg.uploadKey) throw new Error("上传结果缺少 uploadKey");
      await onUploaded(pkg);
    } catch (err) {
      setError(errText(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <span style={{ display: "inline-flex", flexDirection: "column" }}>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        style={{ display: "none" }}
        onChange={(e) => void change(e)}
      />
      <Button
        size="small"
        type={type}
        danger={danger}
        icon={<UploadOutlined />}
        disabled={disabled || busy}
        loading={busy}
        onClick={pick}
      >
        {label}
      </Button>
      {error && (
        <Typography.Text type="danger" style={{ fontSize: 11, marginTop: 2 }}>
          {error}
        </Typography.Text>
      )}
    </span>
  );
}
