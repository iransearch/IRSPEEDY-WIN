# Connection attempt diagnostics

This change only adds logging. Connection decisions, callbacks, exception
handling, cancellation, timers, loading duration, DNS, Core and UI text remain
unchanged. The application version is unchanged.

`stage=connect-attempt` records the Connect click, request-gate wait, test drain,
service preparation, previous-service cleanup, runtime validation, Core dispatch,
configuration construction, system proxy/Proxifier setup and UI presentation.
The existing service/connection identity joins UI requests to background Core
startup. `requestVersion` is the existing UI version, or -1 for a background
component without that UI version. Each local trace also has a generated `trace`
identity, fixed `origin`/`phase`/`outcome`, and elapsed time.

An accepted Core start is recorded separately from callback dispatch, successful
row marking and navigation to the status view. A shown message has a separate
`origin=ui-banner` record with caller method names (without file paths). Success callbacks are retained in these new events even
though the existing log policy omits routine success-only UI callback records.

`connect-attempt-error` identifies the phase which threw. Correlate its `errorId`
with up to eight `connect-attempt-exception` records: exception type, HRESULT,
native/Socket/WebException codes, parent index and up to eight method frames.
Raw messages, links, usernames, passwords, config, file paths and source locations
are excluded. Salted message fingerprints and fixed reason classifications
correlate a startup exception with its callback before the UI substitutes the
existing generic error. An unclassified reason is not proof of a network fault.

The previously silent tunnel-start and Proxifier-start catch blocks also submit
the original exception through the existing sanitized Sentry capture path.
Callers which already report their exceptions do not add a second capture here.
No new Sentry fields, SDK configuration or approval/UI behavior is introduced.
Writes use the existing bounded asynchronous diagnostic queue; logging failures
are swallowed and do not replace the original callback or exception.

For a new report, follow a `connect-click` request version through `ui-request`,
then its service/connection identity through `tunnel-start`, `ui-callback` and
`ui-result`. A missing following phase narrows where execution stopped; it does
not itself establish a failure if the request was canceled or superseded. Reports
from older builds cannot gain these records retroactively.
