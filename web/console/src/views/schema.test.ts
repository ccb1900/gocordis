import { describe, expect, it } from "vitest";
import { matchFilter, resolveParams, type Filter } from "./schema";

describe("matchFilter", () => {
  const row = { status: "Succeeded", filesFailed: 2, records: "17" };

  it("matches membership clauses", () => {
    const f: Filter = { key: "status", in: ["Failed", "Pending"] };
    expect(matchFilter(f, row)).toBe(false);
    expect(matchFilter(f, { ...row, status: "Failed" })).toBe(true);
  });

  it("matches numeric threshold clauses", () => {
    expect(matchFilter({ key: "filesFailed", gt: 0 }, row)).toBe(true);
    expect(matchFilter({ key: "filesFailed", gt: 0 }, { ...row, filesFailed: 0 })).toBe(false);
    // 数字阈值容忍字符串数值（hub JSON 里的数值形态不保证）。
    expect(matchFilter({ key: "records", gt: 10 }, row)).toBe(true);
  });

  it("unions clauses under anyOf", () => {
    const f: Filter = {
      anyOf: [
        { key: "status", in: ["Failed", "Pending"] },
        { key: "filesFailed", gt: 0 },
      ],
    };
    // 部分成功（整体 Succeeded 但有失败文件）也必须上榜——
    // "需要处理"要能回答"到底是哪些文件失败"。
    expect(matchFilter(f, row)).toBe(true);
    expect(matchFilter(f, { ...row, filesFailed: 0 })).toBe(false);
    expect(matchFilter(f, { ...row, status: "Pending", filesFailed: 0 })).toBe(true);
  });
});

describe("resolveParams", () => {
  it("stays dormant without a full focus", () => {
    expect(resolveParams({ sourceId: "$focus.sourceId" }, null)).toBeNull();
    expect(
      resolveParams({ sourceId: "$focus.sourceId", date: "$focus.date" }, { sourceId: "s", date: "" })
    ).toBeNull();
  });

  it("resolves a complete focus", () => {
    expect(
      resolveParams({ sourceId: "$focus.sourceId", date: "$focus.date" }, { sourceId: "s", date: "2026-09-20" })
    ).toEqual({ sourceId: "s", date: "2026-09-20" });
  });
});
