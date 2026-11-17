# RMVC 3.1.0

Commands can hide progress before opening another dialog and cancel specific root commands in their own facade.

- Await `base.HideProgress()` to wait for the facade's progress completion command before continuing. A later explicit progress report can show progress again in the same chain.
- Root completion closes progress that is still visible. If the chain already hid it, completion leaves it hidden.
- Use `base.AbortRootCommands<TRootCommand>()` inside a command to request cooperative cancellation of admitted roots of that exact type in the current facade.

**Compatibility:** `RFacade.AbortCommands<TFacade>()` is now internal. Move scoped cancellation into a command and target the root type there. The public static `RFacade.AbortAllCommands()` still supports application-wide cancellation.

**Samples:** Use the verified README quickstart for the current API. The older repository samples still await migration.

**Downloads:** `RMVC.dll` targeting .NET Standard 2.0 and `seanstardev.RMVC.3.1.0.nupkg`.
