import {
  describe,
  expect,
  it
} from "vitest";

import {
  buildLocationSearchPath,
  hasLocationSearchCriteria,
  moveFavoriteIds
} from "./smart-location-picker";

describe(
  "hasLocationSearchCriteria",
  ()=>{
    it(
      "returns false when all user search fields are blank",
      ()=>{
        expect(
          hasLocationSearchCriteria({
            query:" ",
            city:"",
            district:""
          })
        ).toBe(false);
      }
    );

    it(
      "returns true when any user search field has a value",
      ()=>{
        expect(
          hasLocationSearchCriteria({
            query:"中寮",
            city:"",
            district:""
          })
        ).toBe(true);

        expect(
          hasLocationSearchCriteria({
            query:"",
            city:"南投縣",
            district:""
          })
        ).toBe(true);
      }
    );
  }
);

describe(
  "buildLocationSearchPath",
  ()=>{
    it(
      "builds server-side search parameters",
      ()=>{
        const path=
          buildLocationSearchPath({
            query:" 中寮 ",
            city:"南投縣",
            district:"中寮鄉",
            projectId:12,
            page:2,
            pageSize:20
          });

        const query=
          new URLSearchParams(
            path.split("?")[1]
          );

        expect(
          path.startsWith(
            "/locations/search?"
          )
        ).toBe(true);

        expect(query.get("q"))
          .toBe("中寮");

        expect(query.get("city"))
          .toBe("南投縣");

        expect(query.get("district"))
          .toBe("中寮鄉");

        expect(query.get("projectId"))
          .toBe("12");

        expect(query.get("page"))
          .toBe("2");

        expect(query.get("pageSize"))
          .toBe("20");
      }
    );

    it(
      "omits blank optional filters",
      ()=>{
        const path=
          buildLocationSearchPath({
            query:" ",
            page:1,
            pageSize:20
          });

        const query=
          new URLSearchParams(
            path.split("?")[1]
          );

        expect(query.has("q"))
          .toBe(false);

        expect(query.has("city"))
          .toBe(false);

        expect(query.has("district"))
          .toBe(false);

        expect(query.has("projectId"))
          .toBe(false);
      }
    );
  }
);

describe(
  "moveFavoriteIds",
  ()=>{
    it(
      "moves a favorite upward",
      ()=>{
        expect(
          moveFavoriteIds(
            [10,20,30],
            20,
            -1
          )
        ).toEqual(
          [20,10,30]
        );
      }
    );

    it(
      "moves a favorite downward",
      ()=>{
        expect(
          moveFavoriteIds(
            [10,20,30],
            20,
            1
          )
        ).toEqual(
          [10,30,20]
        );
      }
    );

    it(
      "does not move beyond boundary",
      ()=>{
        const ids=[10,20,30];

        expect(
          moveFavoriteIds(
            ids,
            10,
            -1
          )
        ).toBe(ids);
      }
    );
  }
);
