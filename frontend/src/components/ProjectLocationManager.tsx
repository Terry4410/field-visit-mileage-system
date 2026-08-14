import {
  useEffect,
  useMemo,
  useState
} from "react";

import {api} from "../api";

import type {
  ProjectLocationAdminItem,
  ProjectLocationCandidateResult
} from "../types";

type Props={
  projectId:number;
  projectName:string;
};

function addressOf(
  item:ProjectLocationAdminItem
){
  return item.address
    ||item.plusCode
    ||"未提供地址";
}

function areaOf(
  item:ProjectLocationAdminItem
){
  return [
    item.city,
    item.district
  ].filter(Boolean).join("");
}

export default function ProjectLocationManager({
  projectId,
  projectName
}:Props){
  const [selected,setSelected]=
    useState<ProjectLocationAdminItem[]>([]);

  const [query,setQuery]=useState("");

  const [candidates,setCandidates]=
    useState<ProjectLocationAdminItem[]>([]);

  const [candidateTotal,setCandidateTotal]=
    useState(0);

  const [loading,setLoading]=useState(false);
  const [saving,setSaving]=useState(false);
  const [message,setMessage]=useState("");

  const selectedIds=useMemo(
    ()=>new Set(
      selected.map(x=>x.locationId)
    ),
    [selected]
  );

  const loadAssigned=async()=>{
    setLoading(true);
    setMessage("");

    try{
      setSelected(
        await api<ProjectLocationAdminItem[]>(
          `/admin/projects/${projectId}/locations`
        )
      );
    }catch(e){
      setMessage(
        e instanceof Error
          ?e.message
          :"固定地點載入失敗。"
      );
    }finally{
      setLoading(false);
    }
  };

  useEffect(()=>{
    void loadAssigned();
  },[projectId]);

  useEffect(()=>{
    const keyword=query.trim();

    if(!keyword){
      setCandidates([]);
      setCandidateTotal(0);
      return;
    }

    const timer=
      window.setTimeout(
        async()=>{
          try{
            const params=
              new URLSearchParams({
                q:keyword,
                page:"1",
                pageSize:"20"
              });

            const result=
              await api<ProjectLocationCandidateResult>(
                `/admin/projects/${projectId}/location-candidates?${params.toString()}`
              );

            setCandidates(result.items);
            setCandidateTotal(result.totalCount);
          }catch(e){
            setMessage(
              e instanceof Error
                ?e.message
                :"可用地點搜尋失敗。"
            );
          }
        },
        350
      );

    return()=>{
      window.clearTimeout(timer);
    };
  },[
    query,
    projectId
  ]);

  const add=(
    item:ProjectLocationAdminItem
  )=>{
    if(selectedIds.has(item.locationId))
      return;

    setSelected(rows=>[
      ...rows,
      item
    ]);

    setMessage("");
  };

  const remove=(
    locationId:number
  )=>{
    setSelected(rows=>
      rows.filter(
        x=>x.locationId!==locationId
      )
    );

    setMessage("");
  };

  const save=async()=>{
    setSaving(true);
    setMessage("");

    try{
      const rows=
        await api<ProjectLocationAdminItem[]>(
          `/admin/projects/${projectId}/locations`,
          {
            method:"PUT",
            body:JSON.stringify({
              locationIds:
                selected.map(
                  x=>x.locationId
                )
            })
          }
        );

      setSelected(rows);
      setMessage(
        `固定地點已儲存，共 ${rows.length} 筆。`
      );
    }catch(e){
      setMessage(
        e instanceof Error
          ?e.message
          :"固定地點儲存失敗。"
      );
    }finally{
      setSaving(false);
    }
  };

  return(
    <div className="project-location-manager">
      <div className="section-title">
        <div>
          <h3>專案固定地點</h3>
          <div className="sub">
            {projectName}｜清單型專案只會讓外訪員選擇這裡設定的正式地點。
          </div>
        </div>

        <span className={
          `pill ${
            selected.length===0
              ?"warn"
              :"ok"
          }`
        }>
          已選 {selected.length} 筆
        </span>
      </div>

      {selected.length===0&&
        <div className="note warn-note">
          ⚠ 尚未設定固定地點。外訪員使用此清單型專案時，
          將搜尋不到任何專案正式地點。
        </div>
      }

      <div className="field project-location-search">
        <label>搜尋可加入的正式地點</label>
        <input
          value={query}
          onChange={e=>
            setQuery(e.target.value)
          }
          placeholder="輸入地點名稱、地址、縣市或鄉鎮"
        />
      </div>

      {query.trim()&&
        <div className="project-location-candidates">
          <div className="section-title compact-section-title">
            <strong>搜尋結果</strong>
            <span className="pill">
              {candidateTotal} 筆
            </span>
          </div>

          {candidates.map(item=>{
            const already=
              selectedIds.has(
                item.locationId
              );

            return(
              <div
                key={item.locationId}
                className="project-location-choice"
              >
                <div>
                  <strong>
                    {item.locationName}
                  </strong>
                  <small>
                    {areaOf(item)}
                    {areaOf(item)?"｜":""}
                    {addressOf(item)}
                  </small>
                </div>

                <button
                  type="button"
                  className={
                    `btn small ${
                      already
                        ?"secondary"
                        :"outline"
                    }`
                  }
                  disabled={already}
                  onClick={()=>add(item)}
                >
                  {already
                    ?"已加入"
                    :"加入"}
                </button>
              </div>
            );
          })}

          {!loading
            &&candidates.length===0
            &&<div className="empty compact-empty">
              查無符合條件且可加入此專案的正式地點。
            </div>
          }
        </div>
      }

      <div className="project-selected-locations">
        <div className="section-title compact-section-title">
          <strong>目前固定地點</strong>
          <span className="pill">
            {selected.length} 筆
          </span>
        </div>

        {selected.map(
          (item,index)=>
            <div
              key={item.locationId}
              className="project-location-choice"
            >
              <div className="project-location-index">
                {index+1}
              </div>

              <div>
                <strong>
                  {item.locationName}
                </strong>
                <small>
                  {areaOf(item)}
                  {areaOf(item)?"｜":""}
                  {addressOf(item)}
                </small>
              </div>

              <button
                type="button"
                className="btn small outline"
                onClick={()=>
                  remove(item.locationId)
                }
              >
                移除
              </button>
            </div>
        )}

        {!loading
          &&selected.length===0
          &&<div className="empty compact-empty">
            目前沒有固定地點。
          </div>
        }
      </div>

      {message&&
        <div className={
          `note ${
            message.includes("已儲存")
              ?"ok-note"
              :""
          }`
        }>
          {message}
        </div>
      }

      <button
        type="button"
        className="btn ok"
        disabled={saving||loading}
        onClick={()=>void save()}
      >
        {saving
          ?"儲存中…"
          :"儲存固定地點"}
      </button>
    </div>
  );
}
