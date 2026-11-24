import { describe, expect, it } from "vitest";
import { formatDateSlash, parseDateSlash } from "./date-slash";

describe("project filter date display YYYY/MM/DD", () => {
  it("displays API ISO dates with slashes, and blank stays blank", () => {
    expect(formatDateSlash("2026-11-24")).toBe("2026/11/24");
    expect(formatDateSlash("")).toBe("");
  });

  it("converts a complete valid display date back to exact ISO for the query", () => {
    expect(parseDateSlash("2026/11/24")).toBe("2026-11-24");
    expect(parseDateSlash(" 2026/11/24 ")).toBe("2026-11-24");
    expect(parseDateSlash("2024/02/29")).toBe("2024-02-29");
    expect(parseDateSlash("2000/02/29")).toBe("2000-02-29");
    expect(parseDateSlash("0001/01/01")).toBe("0001-01-01");
  });

  it.each([
    "2026-11-24", "2026/月/24", "2026/1/24", "2026/11/2",
    "2026/11/240", "2026/00/01", "2026/13/01", "2026/11/00",
    "2026/11/31", "2026/02/29", "1900/02/29", "0000/01/01",
    "2026/04/31", "invalid"
  ])("rejects malformed or impossible date %s instead of sending to API", value => {
    expect(parseDateSlash(value)).toBeNull();
  });

  it("preserves lexicographic chronological comparisons on unchanged ISO values", () => {
    const start = parseDateSlash("2026/11/24")!;
    const end = parseDateSlash("2026/12/01")!;
    expect(start < end).toBe(true);
    expect(formatDateSlash(start)).toBe("2026/11/24");
  });
});
