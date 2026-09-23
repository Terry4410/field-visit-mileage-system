# 外訪行程與里程管理系統 v1.8.0 — Business UAT Sign-off

> **目前狀態**
>
> - Technical Readiness：**PASS**
> - Business UAT：**READY TO BEGIN / NOT YET SIGNED OFF**
> - 本文件供 Business UAT 完成後正式記錄驗收決策；目前沒有任何選項預先勾選。

## UAT 基本資料

| 項目 | 內容 |
|---|---|
| Release | v1.8.0 UAT Candidate |
| Environment | UAT |
| URL | https://terry4410.github.io/field-visit-mileage-system/ |
| Technical baseline SHA | 843840abde67256782bdcb53efde01faf44c516f |
| DB Schema | 1.8.0-007 |
| Final Technical Smoke | RUN_ID=35885769760；9/9 PASS |

## 執行摘要

| 欄位 | 填寫內容 |
|---|---|
| UAT Start Date |  |
| UAT End Date |  |
| Business Owner |  |
| UAT Coordinator |  |
| Participating Testers |  |

### 測試與問題統計

| 項目 | 數量 |
|---|---:|
| P0 Total |  |
| P0 Pass |  |
| P0 Fail |  |
| P0 Blocked |  |
| P1 Total |  |
| Open Blocker |  |
| Open Major |  |
| Open Minor |  |
| Open Question |  |

## 未結問題

| Issue ID | Severity | Description | Owner | Disposition | Target Date |
|---|---|---|---|---|---|
|  |  |  |  |  |  |

如無未結問題，請填寫「None」，不要刪除本節。

## Known Limitations / Accepted Items

以下項目為本次 UAT 已知範圍限制，是否接受仍需由 Business Owner 於 UAT 結束時確認：

- UAT only。
- Demo authentication。
- Mock Route Provider。
- Real Google route accuracy excluded。
- Production Entra ID excluded。
- Production deployment excluded。
- Production data migration excluded。
- v1.8.0 master-data import is separately governed。

## Business Decision

請在 Business UAT 完成並檢查所有 P0、BLOCKER、MAJOR 與 known items 後，由 Business Owner 勾選 **一項**：

- [ ] **ACCEPT — Business UAT passed**
- [ ] **ACCEPT WITH OPEN ITEMS — no Blocker; remaining items accepted with follow-up**
- [ ] **RETEST REQUIRED**

> 不可在測試尚未完成前預選任何結果。

## Sign-off

### Business Owner

Name：  
Date：  
Comments：  

Signature / Approval Record：  

### UAT Coordinator

Name：  
Date：  
Comments：  

Signature / Approval Record：  

---

完成本文件前，請再次確認：

- 所有 mandatory P0 cases 均已執行。
- 沒有 unresolved BLOCKER。
- MAJOR issues 已有 disposition。
- Business rule questions 已解決，或已列入明確接受的 known items。
- FAIL / BLOCKED cases 均有 Issue ID。
- Business Owner 的最終決策已明確記錄。

在 Business Owner 正式簽核前，本 release 的 Business UAT 狀態仍為：

**READY TO BEGIN / NOT YET SIGNED OFF**
