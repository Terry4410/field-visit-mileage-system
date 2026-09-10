import type { Project, VisitType } from './types';

export type ProjectEditor = {
  teamId: number | null;
  projectCode: string;
  projectName: string;
  description: string | null;
  locationMode: string;
  startDate: string | null;
  endDate: string | null;
};

export function projectSavePayload(value: ProjectEditor, existing?: Project | null) {
  return {
    teamId: value.teamId,
    projectCode: value.projectCode,
    projectName: value.projectName,
    description: value.description,
    locationMode: value.locationMode,
    startDate: value.startDate,
    endDate: value.endDate,
    ...(existing ? { rowVersion: existing.rowVersion } : {})
  };
}

export type VisitTypeEditor = {
  visitTypeCode: string;
  visitTypeName: string;
  description: string | null;
};

export function visitTypeSavePayload(value: VisitTypeEditor, existing?: VisitType | null) {
  return {
    visitTypeCode: value.visitTypeCode,
    visitTypeName: value.visitTypeName,
    description: value.description,
    ...(existing ? { rowVersion: existing.rowVersion } : {})
  };
}

export function activeVisitTypes(rows: VisitType[]) {
  return rows.filter(x => x.isActive).sort((a, b) => a.sortOrder - b.sortOrder || a.visitTypeId - b.visitTypeId);
}

export function reorderExpectedOrder(rows: VisitType[], visitTypeId: number, direction: 'up' | 'down') {
  const active = activeVisitTypes(rows);
  const index = active.findIndex(x => x.visitTypeId === visitTypeId);
  if (index < 0) return active.map(x => x.visitTypeId);
  const target = direction === 'up' ? index - 1 : index + 1;
  if (target < 0 || target >= active.length) return active.map(x => x.visitTypeId);
  const ids = active.map(x => x.visitTypeId);
  [ids[index], ids[target]] = [ids[target], ids[index]];
  return ids;
}
