import {
  useEffect,
  useMemo,
  useRef,
  useState
} from "react";

import {api} from "../api";

import {
  buildLocationSearchPath,
  hasLocationSearchCriteria,
  moveFavoriteIds
} from "../smart-location-picker";

import type {
  LocationFavoriteItem,
  LocationNearbyItem,
  LocationRecentItem,
  LocationSearchItem,
  LocationSearchResult,
  SmartLocationItem
} from "../types";

type PickerTab=
  |"search"
  |"favorites"
  |"recent"
  |"nearby";

interface Props{
  projectId?:number;
  selectedLocationId?:number;
  onSelect:(location:SmartLocationItem)=>void;
}

const TAB_LABELS:Record<PickerTab,string>={
  search:"搜尋",
  favorites:"常用",
  recent:"最近",
  nearby:"附近"
};

function locationArea(
  item:SmartLocationItem
){
  return [
    item.city,
    item.district
  ].filter(Boolean).join("");
}

function locationAddress(
  item:SmartLocationItem
){
  return item.address
    ||item.plusCode
    ||"未提供地址";
}

export default function SmartLocationPicker({
  projectId,
  selectedLocationId,
  onSelect
}:Props){
  const [tab,setTab]=useState<PickerTab>("search");

  const [query,setQuery]=useState("");
  const [city,setCity]=useState("");
  const [district,setDistrict]=useState("");

  const [searchRows,setSearchRows]=
    useState<LocationSearchItem[]>([]);

  const [searchPage,setSearchPage]=
    useState(1);

  const [searchTotal,setSearchTotal]=
    useState(0);

  const [hasNextPage,setHasNextPage]=
    useState(false);

  const [favoriteRows,setFavoriteRows]=
    useState<LocationFavoriteItem[]>([]);

  const [favoriteIds,setFavoriteIds]=
    useState<Set<number>>(
      new Set<number>()
    );

  const [recentRows,setRecentRows]=
    useState<LocationRecentItem[]>([]);

  const [nearbyRows,setNearbyRows]=
    useState<LocationNearbyItem[]>([]);

  const [loading,setLoading]=useState(false);
  const [geoBusy,setGeoBusy]=useState(false);
  const [favoriteBusyId,setFavoriteBusyId]=
    useState<number|null>(null);

  const [message,setMessage]=useState("");

  const searchSequence=useRef(0);

  const visibleTabs=useMemo<PickerTab[]>(
    ()=>projectId
      ?["search","nearby"]
      :["search","favorites","recent","nearby"],
    [projectId]
  );

  useEffect(()=>{
    if(
      projectId
      &&(tab==="favorites"||tab==="recent")
    ){
      setTab("search");
    }
  },[projectId,tab]);

  const loadFavorites=async()=>{
    try{
      const rows=
        await api<LocationFavoriteItem[]>(
          "/locations/favorites"
        );

      setFavoriteRows(rows);

      setFavoriteIds(
        new Set(
          rows.map(x=>x.locationId)
        )
      );
    }catch(e){
      setMessage(
        e instanceof Error
          ?e.message
          :"常用地點載入失敗。"
      );
    }
  };

  const loadSearch=async(
    page:number,
    append:boolean
  )=>{
    const seq=++searchSequence.current;

    setLoading(true);
    setMessage("");

    try{
      const result=
        await api<LocationSearchResult>(
          buildLocationSearchPath({
            query,
            city,
            district,
            projectId,
            page,
            pageSize:20
          })
        );

      if(seq!==searchSequence.current)
        return;

      setSearchRows(previous=>{
        if(!append)
          return result.items;

        const seen=
          new Set(
            previous.map(x=>x.locationId)
          );

        return [
          ...previous,
          ...result.items.filter(
            x=>!seen.has(x.locationId)
          )
        ];
      });

      setSearchPage(result.page);
      setSearchTotal(result.totalCount);
      setHasNextPage(result.hasNextPage);
    }catch(e){
      if(seq!==searchSequence.current)
        return;

      setMessage(
        e instanceof Error
          ?e.message
          :"地點搜尋失敗。"
      );
    }finally{
      if(seq===searchSequence.current)
        setLoading(false);
    }
  };

  const loadRecent=async()=>{
    setLoading(true);
    setMessage("");

    try{
      setRecentRows(
        await api<LocationRecentItem[]>(
          "/locations/recent?limit=20"
        )
      );
    }catch(e){
      setMessage(
        e instanceof Error
          ?e.message
          :"最近地點載入失敗。"
      );
    }finally{
      setLoading(false);
    }
  };

  useEffect(()=>{
    void loadFavorites();
  },[]);

  useEffect(()=>{
    const hasCriteria=
      hasLocationSearchCriteria({
        query,
        city,
        district
      });

    if(!hasCriteria){
      searchSequence.current++;
      setSearchRows([]);
      setSearchPage(1);
      setSearchTotal(0);
      setHasNextPage(false);
      setLoading(false);
      return;
    }

    const timer=
      window.setTimeout(
        ()=>{
          void loadSearch(1,false);
        },
        350
      );

    return()=>{
      window.clearTimeout(timer);
    };
  },[
    query,
    city,
    district,
    projectId
  ]);

  const switchTab=(
    next:PickerTab
  )=>{
    setTab(next);
    setMessage("");

    if(next==="favorites")
      void loadFavorites();

    if(next==="recent")
      void loadRecent();
  };

  const toggleFavorite=async(
    item:SmartLocationItem
  )=>{
    setFavoriteBusyId(item.locationId);
    setMessage("");

    try{
      if(favoriteIds.has(item.locationId)){
        await api<void>(
          `/locations/${item.locationId}/favorite`,
          {method:"DELETE"}
        );
      }else{
        await api<void>(
          `/locations/${item.locationId}/favorite`,
          {method:"POST"}
        );
      }

      await loadFavorites();
    }catch(e){
      setMessage(
        e instanceof Error
          ?e.message
          :"常用地點更新失敗。"
      );
    }finally{
      setFavoriteBusyId(null);
    }
  };

  const moveFavorite=async(
    locationId:number,
    delta:-1|1
  )=>{
    const current=
      favoriteRows.map(x=>x.locationId);

    const next=
      moveFavoriteIds(
        current,
        locationId,
        delta
      );

    if(next===current)
      return;

    const lookup=
      new Map(
        favoriteRows.map(
          x=>[x.locationId,x]
        )
      );

    setFavoriteRows(
      next
        .map(id=>lookup.get(id))
        .filter(
          (x):x is LocationFavoriteItem=>Boolean(x)
        )
        .map(
          (x,index)=>({
            ...x,
            sortOrder:index
          })
        )
    );

    try{
      await api<void>(
        "/locations/favorites/order",
        {
          method:"PUT",
          body:JSON.stringify({
            locationIds:next
          })
        }
      );

      await loadFavorites();
    }catch(e){
      await loadFavorites();

      setMessage(
        e instanceof Error
          ?e.message
          :"常用地點排序失敗。"
      );
    }
  };

  const findNearby=()=>{
    setMessage("");

    if(!navigator.geolocation){
      setMessage(
        "目前瀏覽器不支援定位；仍可使用搜尋功能。"
      );
      return;
    }

    setGeoBusy(true);

    navigator.geolocation.getCurrentPosition(
      async position=>{
        try{
          const params=
            new URLSearchParams({
              latitude:
                String(
                  position.coords.latitude
                ),
              longitude:
                String(
                  position.coords.longitude
                ),
              limit:"20"
            });

          if(projectId)
            params.set(
              "projectId",
              String(projectId)
            );

          const rows=
            await api<LocationNearbyItem[]>(
              `/locations/nearby?${params.toString()}`
            );

          setNearbyRows(rows);

          if(rows.length===0)
            setMessage(
              "目前沒有可使用且具有座標的地點。"
            );
        }catch(e){
          setMessage(
            e instanceof Error
              ?e.message
              :"附近地點查詢失敗。"
          );
        }finally{
          setGeoBusy(false);
        }
      },
      error=>{
        setGeoBusy(false);

        if(error.code===error.PERMISSION_DENIED){
          setMessage(
            "未允許定位；不影響搜尋、常用及最近地點功能。"
          );
        }else if(error.code===error.TIMEOUT){
          setMessage(
            "定位逾時；可重新嘗試或改用搜尋功能。"
          );
        }else{
          setMessage(
            "目前無法取得定位；仍可使用搜尋功能。"
          );
        }
      },
      {
        enableHighAccuracy:true,
        timeout:10000,
        maximumAge:60000
      }
    );
  };

  const renderChoice=(
    item:SmartLocationItem,
    extra?:string
  )=>{
    const selected=
      selectedLocationId===item.locationId;

    const favorite=
      favoriteIds.has(item.locationId);

    return(
      <div
        key={item.locationId}
        className={
          `smart-location-row ${
            selected?"selected":""
          }`
        }
      >
        <button
          type="button"
          className="smart-location-main"
          onClick={()=>onSelect(item)}
        >
          <strong>
            {item.locationName}
          </strong>

          <span>
            {locationArea(item)}
            {locationArea(item)?"｜":""}
            {locationAddress(item)}
          </span>

          {extra&&
            <small>{extra}</small>
          }
        </button>

        <button
          type="button"
          className={
            `smart-favorite-button ${
              favorite?"active":""
            }`
          }
          disabled={
            favoriteBusyId===item.locationId
          }
          aria-label={
            favorite
              ?"移除常用地點"
              :"加入常用地點"
          }
          title={
            favorite
              ?"移除常用地點"
              :"加入常用地點"
          }
          onClick={()=>
            void toggleFavorite(item)
          }
        >
          {favorite?"★":"☆"}
        </button>
      </div>
    );
  };

  return(
    <div className="smart-location-picker">
      <div className="smart-location-tabs">
        {visibleTabs.map(x=>
          <button
            key={x}
            type="button"
            className={
              tab===x?"active":""
            }
            onClick={()=>
              switchTab(x)
            }
          >
            {TAB_LABELS[x]}
          </button>
        )}
      </div>

      {projectId&&
        <div className="note smart-project-note">
          目前為專案地點模式；搜尋與附近只會顯示此專案允許的正式地點。
        </div>
      }

      {message&&
        <div className="note warn-note smart-location-message">
          {message}
        </div>
      }

      {tab==="search"&&<>
        <div className="smart-location-search">
          <div className="field smart-search-keyword">
            <label>搜尋地點</label>
            <input
              value={query}
              onChange={e=>
                setQuery(e.target.value)
              }
              placeholder="輸入客戶名稱、地址、縣市、鄉鎮或統編"
            />
          </div>

          <div className="grid cols-2">
            <div className="field">
              <label>
                縣市
                <span className="optional">
                  選填
                </span>
              </label>
              <input
                value={city}
                onChange={e=>
                  setCity(e.target.value)
                }
                placeholder="例如：南投縣"
              />
            </div>

            <div className="field">
              <label>
                鄉鎮／區
                <span className="optional">
                  選填
                </span>
              </label>
              <input
                value={district}
                onChange={e=>
                  setDistrict(e.target.value)
                }
                placeholder="例如：中寮鄉"
              />
            </div>
          </div>
        </div>

        <div className="section-title smart-result-title">
          <strong>搜尋結果</strong>
          <span className="pill">
            {searchTotal} 筆
          </span>
        </div>

        <div className="smart-location-results">
          {searchRows.map(x=>
            renderChoice(x)
          )}

          {!loading&&
            searchRows.length===0&&
            <div className="empty compact-empty">
              {hasLocationSearchCriteria({
                query,
                city,
                district
              })
                ?"查無符合條件的正式地點。"
                :"請輸入地點名稱、地址、縣市或鄉鎮開始搜尋。"}
            </div>
          }
        </div>

        {loading&&
          <div className="smart-location-loading">
            搜尋中…
          </div>
        }

        {hasNextPage&&
          <button
            type="button"
            className="btn outline full smart-load-more"
            disabled={loading}
            onClick={()=>
              void loadSearch(
                searchPage+1,
                true
              )
            }
          >
            載入更多
          </button>
        }
      </>}

      {tab==="favorites"&&<>
        <div className="section-title smart-result-title">
          <strong>我的常用地點</strong>
          <span className="pill">
            {favoriteRows.length} 筆
          </span>
        </div>

        <div className="smart-location-results">
          {favoriteRows.map(
            (item,index)=>
              <div
                key={item.locationId}
                className={
                  `smart-location-row smart-favorite-row ${
                    selectedLocationId===item.locationId
                      ?"selected"
                      :""
                  }`
                }
              >
                <button
                  type="button"
                  className="smart-location-main"
                  onClick={()=>
                    onSelect(item)
                  }
                >
                  <strong>
                    {item.locationName}
                  </strong>

                  <span>
                    {locationArea(item)}
                    {locationArea(item)?"｜":""}
                    {locationAddress(item)}
                  </span>
                </button>

                <div className="smart-favorite-order">
                  <button
                    type="button"
                    disabled={index===0}
                    aria-label="常用地點上移"
                    onClick={()=>
                      void moveFavorite(
                        item.locationId,
                        -1
                      )
                    }
                  >
                    ↑
                  </button>

                  <button
                    type="button"
                    disabled={
                      index===favoriteRows.length-1
                    }
                    aria-label="常用地點下移"
                    onClick={()=>
                      void moveFavorite(
                        item.locationId,
                        1
                      )
                    }
                  >
                    ↓
                  </button>

                  <button
                    type="button"
                    className="active"
                    aria-label="移除常用地點"
                    onClick={()=>
                      void toggleFavorite(item)
                    }
                  >
                    ★
                  </button>
                </div>
              </div>
          )}

          {!loading&&
            favoriteRows.length===0&&
            <div className="empty compact-empty">
              尚未加入常用地點。可在搜尋結果點選 ☆ 收藏。
            </div>
          }
        </div>
      </>}

      {tab==="recent"&&<>
        <div className="section-title smart-result-title">
          <strong>最近拜訪</strong>
          <span className="pill">
            {recentRows.length} 筆
          </span>
        </div>

        <div className="smart-location-results">
          {recentRows.map(x=>
            renderChoice(
              x,
              `最近拜訪：${x.lastVisitedOn}`
            )
          )}

          {!loading&&
            recentRows.length===0&&
            <div className="empty compact-empty">
              尚無可顯示的歷史拜訪地點。
            </div>
          }
        </div>
      </>}

      {tab==="nearby"&&<>
        <div className="note smart-nearby-note">
          定位只會在你按下「尋找附近地點」時取得一次，
          僅用於本次距離排序，不會儲存目前位置。
        </div>

        <button
          type="button"
          className="btn secondary full smart-nearby-button"
          disabled={geoBusy}
          onClick={findNearby}
        >
          {geoBusy
            ?"正在取得位置…"
            :"◎ 尋找附近地點"}
        </button>

        <div className="smart-location-results">
          {nearbyRows.map(x=>
            renderChoice(
              x,
              `距離約 ${x.distanceKm.toFixed(2)} km`
            )
          )}

          {!geoBusy&&
            nearbyRows.length===0&&
            <div className="empty compact-empty">
              按上方按鈕取得本次附近地點。
            </div>
          }
        </div>
      </>}
    </div>
  );
}
