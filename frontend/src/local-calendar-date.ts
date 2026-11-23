/** Calendar date in the browser's local time zone, not UTC. */
export function getLocalCalendarDate(now:Date=new Date()):string {
  return [
    String(now.getFullYear()).padStart(4,"0"),
    String(now.getMonth()+1).padStart(2,"0"),
    String(now.getDate()).padStart(2,"0")
  ].join("-");
}
