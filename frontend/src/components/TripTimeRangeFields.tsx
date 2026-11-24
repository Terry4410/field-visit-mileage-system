import {tripHourOptions, tripMinuteOptions} from '../trip-time-options';

type Props = {
  start: string;
  end: string;
  showAllHours: boolean;
  showAllMinutes: boolean;
  onStartChange: (value: string) => void;
  onEndChange: (value: string) => void;
  onShowAllHoursChange: (value: boolean) => void;
  onShowAllMinutesChange: (value: boolean) => void;
};

function updateTime(current: string, part: 'hour' | 'minute', value: string) {
  const [hour = '00', minute = '00'] = current.split(':');
  return part === 'hour' ? `${value}:${minute}` : `${hour}:${value}`;
}

export default function TripTimeRangeFields({
  start,
  end,
  showAllHours,
  showAllMinutes,
  onStartChange,
  onEndChange,
  onShowAllHoursChange,
  onShowAllMinutesChange
}: Props) {
  const hours = tripHourOptions(showAllHours, [start.slice(0, 2), end.slice(0, 2)]);
  const minutes = tripMinuteOptions(showAllMinutes, [start.slice(3, 5), end.slice(3, 5)]);

  const field = (
    label: string,
    value: string,
    onChange: (value: string) => void
  ) => <div className="field">
    <label>{label}</label>
    <div className="time-select">
      <select aria-label={`${label}－時`} value={value.slice(0, 2)} onChange={event => onChange(updateTime(value, 'hour', event.target.value))}>
        {hours.map(hour => <option key={hour} value={hour}>{hour}</option>)}
      </select>
      <span>時</span>
      <select aria-label={`${label}－分`} value={value.slice(3, 5)} onChange={event => onChange(updateTime(value, 'minute', event.target.value))}>
        {minutes.map(minute => <option key={minute} value={minute}>{minute}</option>)}
      </select>
      <span>分</span>
    </div>
  </div>;

  return <div className="trip-time-range span-2">
    <div className="trip-time-fields">
      {field('出發時間', start, onStartChange)}
      {field('結束時間', end, onEndChange)}
    </div>
    <div className="trip-time-options">
      <label className="check-row"><input type="checkbox" checked={showAllHours} onChange={event => onShowAllHoursChange(event.target.checked)}/>顯示其他時段（00～23 時）</label>
      <label className="check-row"><input type="checkbox" checked={showAllMinutes} onChange={event => onShowAllMinutesChange(event.target.checked)}/>精確至分鐘（00～59 分）</label>
    </div>
  </div>;
}
