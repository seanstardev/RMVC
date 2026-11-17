# RMVC 3.0.0

A major breaking release that strengthens actor boundaries, simplifies root command submission, and makes application initialisation and shutdown explicit.

- Commands are the framework-provided actor-resolution surface; facades expose command entry points.
- Required model, mediator, child-facade and child-bridge lookups fail with descriptive errors. Deliberate optional lookups remain available where supported.
- Models, mediators, bridges and facade entry points use `Execute(...)` for ordered batches of independent roots. `ExecuteSequentially()` has been removed; separate `Execute(...)` calls can still overlap.
- Added explicit child initialisation scheduling, typed initialisation payloads, and stricter registration and ownership validation.
- Shell is required, shared across the application, and reports lifecycle state. Its built-in message-box contract is now synchronous.
- Added awaitable shutdown with a time budget, command draining and model cleanup. Shutdown-coordinating commands cannot resume actor access or child execution.
- Refined mediator attachment hooks and actor access, and standardised command identifiers and diagnostics.
- Replaced the README with diagrams and a complete, executable quickstart.

Existing applications require migration from the 2.x APIs. Framework creation is permitted only once per runtime; failure does not enable recreation. Models implementing `IRModel` must provide `Task Cleanup()`.

**Samples:** existing repository sample applications have not been migrated to 3.0.0. Use the verified README quickstart as the current reference.

**Downloads:** `RMVC.dll` targeting .NET Standard 2.0 and `seanstardev.RMVC.3.0.0.nupkg`.
