// Fail-closed: requires separate Owner migration, backend and frontend approvals.
export const OPTIONC_APPROVAL_UI_ENABLED: boolean = false;

// Leader first-level review stays disabled outside an explicitly enabled isolated candidate host.
export const OPTIONC_LEADER_REVIEW_UI_ENABLED: boolean = false;

// Owner has not authorized candidate CreateLocation/own requests in UAT.
export const OPTIONC_CREATE_LOCATION_UI_ENABLED: boolean = false;
