// Fail-closed: requires separate Owner migration, backend and frontend approvals.
export const OPTIONC_APPROVAL_UI_ENABLED: boolean = false;

// Leader first-level review stays disabled outside an explicitly enabled isolated candidate host.
export const OPTIONC_LEADER_REVIEW_UI_ENABLED: boolean = false;

// Owner has not authorized candidate CreateLocation/own requests in UAT.
export const OPTIONC_CREATE_LOCATION_UI_ENABLED: boolean = false;

// DirectLowRisk maintenance is independently gated from candidate creation.
// It remains disabled in every deployed environment.
export const OPTIONC_DIRECT_LOWRISK_UI_ENABLED: boolean = false;

// Excel Create -> canonical reviewed Request requires separate Owner UAT gates.
export const OPTIONC_EXCEL_CREATE_UI_ENABLED: boolean = false;

// Excel published Update -> canonical review remains disabled pending Owner UAT gates.
export const OPTIONC_EXCEL_UPDATE_UI_ENABLED: boolean = false;
