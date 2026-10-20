import { describe, expect, it } from "vitest";
import { OPTIONC_APPROVAL_UI_ENABLED, OPTIONC_LEADER_REVIEW_UI_ENABLED } from "./optionc-approval-flags";

describe("Option C Leader review UI", () => {
  it("remains fail-closed by default", () => {
    expect(OPTIONC_APPROVAL_UI_ENABLED).toBe(false);
    expect(OPTIONC_LEADER_REVIEW_UI_ENABLED).toBe(false);
  });
});
