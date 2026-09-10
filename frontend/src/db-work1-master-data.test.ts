import { describe, expect, it } from 'vitest';
import { projectSavePayload, reorderExpectedOrder, visitTypeSavePayload } from './db-work1-master-data';
import type { Project, VisitType } from './types';

const project: Project = {
  projectId: 10,
  teamId: 2,
  projectCode: 'P10',
  projectName: 'Project 10',
  locationMode: 'List',
  isActive: true,
  rowVersion: 'AQIDBA=='
};

const visitTypes: VisitType[] = [
  { visitTypeId: 1, visitTypeCode: 'A', visitTypeName: 'A', sortOrder: 10, isActive: true, rowVersion: 'AQ==' },
  { visitTypeId: 2, visitTypeCode: 'B', visitTypeName: 'B', sortOrder: 20, isActive: true, rowVersion: 'Ag==' },
  { visitTypeId: 3, visitTypeCode: 'C', visitTypeName: 'C', sortOrder: 30, isActive: false, rowVersion: 'Aw==' }
];

describe('D-B Work 1 frontend master-data contracts', () => {
  it('Project ordinary edit carries RowVersion but cannot send IsActive', () => {
    const payload = projectSavePayload({
      teamId: 2, projectCode: 'P10', projectName: 'Updated', description: null,
      locationMode: 'List', startDate: null, endDate: null
    }, project);
    expect(payload).toMatchObject({ rowVersion: 'AQIDBA==' });
    expect(payload).not.toHaveProperty('isActive');
  });

  it('Project create cannot send lifecycle state', () => {
    const payload = projectSavePayload({
      teamId: null, projectCode: 'NEW', projectName: 'New', description: null,
      locationMode: 'List', startDate: null, endDate: null
    });
    expect(payload).not.toHaveProperty('isActive');
    expect(payload).not.toHaveProperty('rowVersion');
  });

  it('VisitType ordinary edit carries RowVersion but cannot send SortOrder/IsActive', () => {
    const payload = visitTypeSavePayload({ visitTypeCode: 'A', visitTypeName: 'A2', description: null }, visitTypes[0]);
    expect(payload).toMatchObject({ rowVersion: 'AQ==' });
    expect(payload).not.toHaveProperty('sortOrder');
    expect(payload).not.toHaveProperty('isActive');
  });

  it('reorder sends complete active membership and excludes inactive rows', () => {
    expect(reorderExpectedOrder(visitTypes, 2, 'up')).toEqual([2, 1]);
    expect(reorderExpectedOrder(visitTypes, 1, 'down')).toEqual([2, 1]);
  });
});
