import {describe,it,expect} from 'vitest';
import {mayEditVisitorDraft} from './location-ownership-ui';
import type {ManagedLocation} from './types';
describe('Visitor draft ownership controls',()=>{
 const row={createdByUserId:7,approvalStatus:'Pending',isActive:false,locationType:'Customer',isTemporary:false} as ManagedLocation;
 it('allows only an owned customer master draft',()=>{
  expect(mayEditVisitorDraft(row,7)).toBe(true);
  for(const changed of [{createdByUserId:8},{createdByUserId:null},{isActive:true},{approvalStatus:'Approved'},{locationType:'Official'},{isTemporary:true},{duplicateOfLocationId:99}])expect(mayEditVisitorDraft({...row,...changed},7)).toBe(false);
 });
});
