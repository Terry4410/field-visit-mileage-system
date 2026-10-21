/// <reference types="vite/client" />
// This opt-in build variant is NONDEPLOY. UI is never a security authority.
export const OPTIONC_ACTIVATION_CANDIDATE = import.meta.env.MODE === "optionc-activation-candidate";
export const OPTIONC_APPROVAL_UI_ENABLED: boolean = OPTIONC_ACTIVATION_CANDIDATE;
export const OPTIONC_LEADER_REVIEW_UI_ENABLED: boolean = OPTIONC_ACTIVATION_CANDIDATE;
export const OPTIONC_CREATE_LOCATION_UI_ENABLED: boolean = OPTIONC_ACTIVATION_CANDIDATE;
export const OPTIONC_DIRECT_LOWRISK_UI_ENABLED: boolean = OPTIONC_ACTIVATION_CANDIDATE;
export const OPTIONC_EXCEL_CREATE_UI_ENABLED: boolean = OPTIONC_ACTIVATION_CANDIDATE;
export const OPTIONC_EXCEL_UPDATE_UI_ENABLED: boolean = OPTIONC_ACTIVATION_CANDIDATE;
