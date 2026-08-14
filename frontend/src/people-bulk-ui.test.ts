import{
  describe,
  expect,
  it
}from"vitest";

import{
  canConfirmPeopleBulk,
  peopleBulkActionLabel,
  peopleBulkHasPartialFailure,
  peopleBulkResultMessage,
  peopleBulkStatusLabel
}from"./people-bulk-ui";

describe("people bulk ui",()=>{
  it("allows confirm only when preview has no errors",()=>{
    expect(
      canConfirmPeopleBulk({
        importBatchId:"batch-1",
        totalCount:2,
        validCount:2,
        errorCount:0,
        requiresRetroactiveConfirmation:false,
        items:[]
      })
    ).toBe(true);

    expect(
      canConfirmPeopleBulk({
        importBatchId:"batch-1",
        totalCount:2,
        validCount:1,
        errorCount:1,
        requiresRetroactiveConfirmation:false,
        items:[]
      })
    ).toBe(false);
  });

  it("rejects empty preview confirm",()=>{
    expect(
      canConfirmPeopleBulk({
        importBatchId:"batch-1",
        totalCount:0,
        validCount:0,
        errorCount:0,
        requiresRetroactiveConfirmation:false,
        items:[]
      })
    ).toBe(false);
  });

  it("translates actions",()=>{
    expect(
      peopleBulkActionLabel("Create")
    ).toBe("新增");

    expect(
      peopleBulkActionLabel("Update")
    ).toBe("更新");

    expect(
      peopleBulkActionLabel("NoChange")
    ).toBe("無異動");
  });

  it("translates preview status",()=>{
    expect(
      peopleBulkStatusLabel("Valid")
    ).toBe("正確");

    expect(
      peopleBulkStatusLabel("Error")
    ).toBe("錯誤");
  });

  it("formats confirm result and detects partial failure",()=>{
    const result={
      importBatchId:"batch-1",
      created:1,
      updated:2,
      unchanged:3,
      failed:1,
      errors:["test"]
    };

    expect(
      peopleBulkResultMessage(result)
    ).toBe(
      "新增 1｜更新 2｜無異動 3｜失敗 1"
    );

    expect(
      peopleBulkHasPartialFailure(result)
    ).toBe(true);
  });
});
