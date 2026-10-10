// A brand-new draft was already hydrated from its creation response.
// Loading it again during route calculation could overwrite a failed
// Google preview and hide the manual fallback input.
export const shouldHydrateTripOnEdit=(editId:string|null,locallyCreatedTripId:string|null):boolean=>
  !!editId&&editId!==locallyCreatedTripId;
