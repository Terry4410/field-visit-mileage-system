export const STANDARD_TRIP_HOURS = Array.from(
  {length: 13},
  (_, index) => String(index + 8).padStart(2, '0')
);

export const STANDARD_TRIP_MINUTES = Array.from(
  {length: 12},
  (_, index) => String(index * 5).padStart(2, '0')
);

const ALL_HOURS = Array.from(
  {length: 24},
  (_, index) => String(index).padStart(2, '0')
);

const ALL_MINUTES = Array.from(
  {length: 60},
  (_, index) => String(index).padStart(2, '0')
);

export function tripTimeOptions(
  standard: readonly string[],
  all: readonly string[],
  expanded: boolean,
  currentValues: readonly string[]
) {
  return Array.from(
    new Set([...(expanded ? all : standard), ...currentValues.filter(Boolean)])
  ).sort();
}

export function tripHourOptions(expanded: boolean, currentValues: readonly string[]) {
  return tripTimeOptions(STANDARD_TRIP_HOURS, ALL_HOURS, expanded, currentValues);
}

export function tripMinuteOptions(expanded: boolean, currentValues: readonly string[]) {
  return tripTimeOptions(STANDARD_TRIP_MINUTES, ALL_MINUTES, expanded, currentValues);
}
