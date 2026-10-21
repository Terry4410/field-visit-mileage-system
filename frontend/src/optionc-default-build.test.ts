import {expect,it} from "vitest";
import * as flags from "./optionc-approval-flags";
it("keeps every Option C entry off in the normal build",()=>{
 expect(flags.OPTIONC_ACTIVATION_CANDIDATE).toBe(false);
 expect([flags.OPTIONC_APPROVAL_UI_ENABLED,flags.OPTIONC_LEADER_REVIEW_UI_ENABLED,
 flags.OPTIONC_CREATE_LOCATION_UI_ENABLED,flags.OPTIONC_DIRECT_LOWRISK_UI_ENABLED,
 flags.OPTIONC_EXCEL_CREATE_UI_ENABLED,flags.OPTIONC_EXCEL_UPDATE_UI_ENABLED]).toEqual([false,false,false,false,false,false]);
});
