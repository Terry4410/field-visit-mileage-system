import {describe, expect, it} from 'vitest';
import {tripHourOptions, tripMinuteOptions} from './trip-time-options';

describe('trip time selector options', () => {
  it('uses the approved default business hours and five-minute increments', () => {
    expect(tripHourOptions(false, ['08', '17'])).toEqual([
      '08', '09', '10', '11', '12', '13', '14', '15', '16', '17', '18', '19', '20'
    ]);
    expect(tripMinuteOptions(false, ['30'])).toEqual([
      '00', '05', '10', '15', '20', '25', '30', '35', '40', '45', '50', '55'
    ]);
  });

  it('preserves historical off-hours and exact minutes when compact options are restored', () => {
    expect(tripHourOptions(false, ['05', '23'])).toContain('05');
    expect(tripHourOptions(false, ['05', '23'])).toContain('23');
    expect(tripMinuteOptions(false, ['33', '47'])).toContain('33');
    expect(tripMinuteOptions(false, ['33', '47'])).toContain('47');
  });

  it('offers every hour and minute only when its independent option is enabled', () => {
    expect(tripHourOptions(true, [])).toHaveLength(24);
    expect(tripMinuteOptions(true, [])).toHaveLength(60);
  });
});
