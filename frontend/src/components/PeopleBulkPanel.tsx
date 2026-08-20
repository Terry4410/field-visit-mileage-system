import{
  useState
}from"react";

import{
  api,
  apiDownload
}from"../api";

import{
  canConfirmPeopleBulk,
  peopleBulkActionLabel,
  peopleBulkHasPartialFailure,
  peopleBulkResultMessage,
  peopleBulkStatusLabel
}from"../people-bulk-ui";

import type{
  PeopleBulkConfirmResult,
  PeopleBulkPreview
}from"../types";

type Props={
  onConfirmed:()=>void|Promise<void>;
};

export default function PeopleBulkPanel({
  onConfirmed
}:Props){
  const[
    file,
    setFile
  ]=useState<File|null>(null);

  const[
    preview,
    setPreview
  ]=useState<PeopleBulkPreview|null>(null);

  const[
    result,
    setResult
  ]=useState<PeopleBulkConfirmResult|null>(null);

  const[
    message,
    setMessage
  ]=useState("");

  const[
    busy,
    setBusy
  ]=useState(false);

  const[
    retroactiveOpen,
    setRetroactiveOpen
  ]=useState(false);

  const download=async(
    path:string,
    fallback:string
  )=>{
    setBusy(true);
    setMessage("");

    try{
      await apiDownload(
        path,
        fallback,
        120000
      );
    }catch(e){
      setMessage(
        e instanceof Error
          ?e.message
          :"下載失敗"
      );
    }finally{
      setBusy(false);
    }
  };

  const downloadCurrent=()=>download(
    "/admin/people/bulk/current.xlsx",
    "人員與權限目前設定.xlsx"
  );

  const downloadTemplate=()=>download(
    "/admin/people/bulk/template.xlsx",
    "人員與權限匯入範例.xlsx"
  );

  const downloadErrors=()=> {
    if(!preview)return Promise.resolve();

    return download(
      `/admin/people/bulk/${preview.importBatchId}/errors.xlsx`,
      "人員與權限匯入錯誤.xlsx"
    );
  };

  const doPreview=async()=>{
    if(!file){
      setMessage(
        "請先選擇 .xlsx、.xls 或 .csv 檔案。"
      );
      return;
    }

    setBusy(true);
    setMessage("");
    setResult(null);

    try{
      const form=
        new FormData();

      form.append(
        "file",
        file
      );

      const value=
        await api<PeopleBulkPreview>(
          "/admin/people/bulk/preview",
          {
            method:"POST",
            body:form
          },
          120000
        );

      setPreview(value);

      if(value.errorCount>0){
        setMessage(
          "預覽完成，但仍有錯誤資料；請下載錯誤 Excel 修正後重新上傳。"
        );
      }else if(
        value.requiresRetroactiveConfirmation
      ){
        setMessage(
          "預覽完成；此批次包含回溯異動，確認更新前需要再次確認。"
        );
      }else{
        setMessage(
          "預覽完成，資料驗證通過。"
        );
      }
    }catch(e){
      setPreview(null);

      setMessage(
        e instanceof Error
          ?e.message
          :"預覽失敗"
      );
    }finally{
      setBusy(false);
    }
  };

  const doConfirm=async(
    confirmRetroactive:boolean
  )=>{
    if(!preview)return;

    setRetroactiveOpen(false);
    setBusy(true);
    setMessage("");

    try{
      const value=
        await api<PeopleBulkConfirmResult>(
          `/admin/people/bulk/${preview.importBatchId}/confirm`,
          {
            method:"POST",
            body:JSON.stringify({
              confirmRetroactive
            })
          },
          120000
        );

      setResult(value);

      await onConfirmed();

      if(
        peopleBulkHasPartialFailure(
          value
        )
      ){
        setMessage(
          "部分資料更新失敗；成功資料已套用，請下載失敗明細修正後重新上傳。"
        );
      }else{
        setMessage(
          "人員與權限批次更新完成。"
        );
      }
    }catch(e){
      setMessage(
        e instanceof Error
          ?e.message
          :"批次更新失敗"
      );
    }finally{
      setBusy(false);
    }
  };

  const requestConfirm=()=>{
    if(
      !preview
      || !canConfirmPeopleBulk(
        preview
      )
      || result
    ){
      return;
    }

    if(
      preview
        .requiresRetroactiveConfirmation
    ){
      setRetroactiveOpen(true);
      return;
    }

    void doConfirm(false);
  };

  return <>
    <div className="card">
      <div className="section-title">
        <div>
          <h2>
            批次維護人員與權限
          </h2>

          <div className="sub">
            上傳只會先建立預覽；
            驗證完成並按下確認後，
            才會正式修改權限。
          </div>
        </div>

        <div className="actions">
          <button
            className="btn small outline"
            disabled={busy}
            onClick={()=>
              void downloadCurrent()
            }
          >
            下載目前設定 Excel
          </button>

          <button
            className="btn small outline"
            disabled={busy}
            onClick={()=>
              void downloadTemplate()
            }
          >
            下載匯入範例
          </button>
        </div>
      </div>

      <div className="actions">
        <input
          type="file"
          accept=".xlsx,.xls,.csv"
          disabled={busy}
          onChange={e=>{
            setFile(
              e.target.files?.[0]
              ||null
            );

            setPreview(null);
            setResult(null);
            setMessage("");
          }}
        />

        <button
          className="btn secondary"
          disabled={busy||!file}
          onClick={()=>
            void doPreview()
          }
        >
          預覽與驗證
        </button>
      </div>

      {message&&
        <div
          className="note"
          style={{marginTop:14}}
        >
          {message}
        </div>
      }

      {preview&&<>
        <div
          className={
            `note ${
              preview.errorCount>0
                ?"danger-note"
                :preview
                    .requiresRetroactiveConfirmation
                  ?"warn-note"
                  :"ok-note"
            }`
          }
          style={{marginTop:14}}
        >
          共 {preview.totalCount} 筆
          ｜正確 {preview.validCount}
          ｜錯誤 {preview.errorCount}

          {preview
            .requiresRetroactiveConfirmation
            ?"｜包含回溯異動"
            :""
          }
        </div>

        <div
          className="table-wrap"
          style={{marginTop:14}}
        >
          <table>
            <thead>
              <tr>
                <th>列</th>
                <th>工作表</th>
                <th>對象</th>
                <th>動作</th>
                <th>Key</th>
                <th>結果</th>
                <th>說明</th>
              </tr>
            </thead>

            <tbody>
              {preview.items
                .slice(0,200)
                .map((x,i)=>{
                  const tone=
                    x.status==="Error"
                      ?"danger"
                      :x.isRetroactive
                        ?"warn"
                        :"ok";

                  return(
                    <tr
                      key={`${x.sheet}-${x.rowNumber}-${i}`}
                    >
                      <td>
                        {x.rowNumber}
                      </td>

                      <td>
                        {x.sheet}
                      </td>

                      <td>
                        {x.entityType}
                      </td>

                      <td>
                        {peopleBulkActionLabel(
                          x.action
                        )}

                        {x.isRetroactive&&
                          <div
                            className="muted"
                          >
                            回溯
                          </div>
                        }
                      </td>

                      <td>
                        {x.displayKey}
                      </td>

                      <td>
                        <span
                          className={
                            `pill ${tone}`
                          }
                        >
                          {peopleBulkStatusLabel(
                            x.status
                          )}
                        </span>
                      </td>

                      <td>
                        {x.message||"—"}
                      </td>
                    </tr>
                  );
                })}
            </tbody>
          </table>
        </div>

        {preview.items.length>200&&
          <div
            className="muted"
            style={{marginTop:8}}
          >
            畫面僅顯示前 200 筆；
            Excel 預覽與確認仍會處理全部資料。
          </div>
        }

        {!result&&
          <div
            className="actions"
            style={{marginTop:14}}
          >
            {preview.errorCount>0&&
              <button
                className="btn outline"
                disabled={busy}
                onClick={()=>
                  void downloadErrors()
                }
              >
                下載錯誤 Excel
              </button>
            }

            <button
              className="btn ok"
              disabled={
                busy
                ||!canConfirmPeopleBulk(
                  preview
                )
              }
              onClick={
                requestConfirm
              }
            >
              確認批次更新
            </button>
          </div>
        }
      </>}

      {result&&
        <div
          className={
            `note ${
              peopleBulkHasPartialFailure(
                result
              )
                ?"danger-note"
                :"ok-note"
            }`
          }
          style={{marginTop:14}}
        >
          <strong>
            {peopleBulkHasPartialFailure(
              result
            )
              ?"部分更新失敗"
              :"批次更新完成"
            }
          </strong>

          <div style={{marginTop:6}}>
            {peopleBulkResultMessage(
              result
            )}
          </div>

          {result.errors.length>0&&
            <ul
              style={{
                marginBottom:0
              }}
            >
              {result.errors
                .slice(0,10)
                .map((x,i)=>
                  <li key={i}>
                    {x}
                  </li>
                )
              }
            </ul>
          }

          {result.failed>0&&
            <div
              className="actions"
              style={{marginTop:12}}
            >
              <button
                className="btn outline"
                disabled={busy}
                onClick={()=>
                  void downloadErrors()
                }
              >
                下載失敗明細 Excel
              </button>
            </div>
          }
        </div>
      }
    </div>

    {retroactiveOpen&&
      <div className="modal">
        <div className="modal-panel">
          <h3>
            確認回溯異動
          </h3>

          <div className="note warn-note">
            此批次包含生效日早於今天的異動。
            確認後可能調整歷史角色、
            小組或授權有效期間。
          </div>

          <p className="muted">
            請確認 Excel 中的
            ChangeEffectiveFrom
            日期正確，再繼續執行。
          </p>

          <div className="modal-sticky-actions">
            <button
              className="btn secondary"
              disabled={busy}
              onClick={()=>
                setRetroactiveOpen(false)
              }
            >
              取消
            </button>

            <button
              className="btn danger"
              disabled={busy}
              onClick={()=>
                void doConfirm(true)
              }
            >
              確認回溯異動並更新
            </button>
          </div>
        </div>
      </div>
    }
  </>;
}
