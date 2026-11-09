// IANA time zone of the browser (or the MAUI web view), e.g. "Europe/Vienna".
export function getTimeZone() {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
}
