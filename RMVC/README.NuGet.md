# RMVC 3.2.0

**Readable, navigable, compiler-checked application workflows.**

RMVC is a small MVC framework for C# applications, targeting .NET Standard 2.0. Commands coordinate models, proxies and mediators through explicit, typed calls. Views own presentation, while an application's abstract `CommandBase` gives its concrete commands a readable vocabulary for the capabilities they use.

```sh
dotnet add package seanstardev.RMVC --version 3.2.0
```

Start with the [complete counter quickstart and diagrams](https://github.com/seanstardev/RMVC/blob/v3.2.0/README.md). It implements the model, mediator, view contract, command base, commands, facade and Shell, with a runnable .NET 9 console host.

## New in 3.2

**Optional command locks** coordinate participating commands through facade-owned gates. Register meaningful `IRCommandLock` instances and declare requirements through `GetLocks()`. RMVC manages acquisition, ancestor reservation reuse and release. This is advanced usage; most applications do not need it.

**Cleanup children after cancellation:** ordinary awaited children can enter `Run()` with inherited abort already set. They retain the shared cancellation token and error history. Required cleanup can run before the command's own cancellation checkpoints.

This child-execution change also applies without command locks. Review commands that previously relied on aborted children being skipped. Child gate waits continue despite inherited cancellation; cleanup is not guaranteed to finish quickly.

Detected circular command-lock waits produce an unhandled `RCommandLockCycleException` independently of logging. Locks do not replace actor thread safety, input validation or database transactions.

See the [3.2.0 release notes](https://github.com/seanstardev/RMVC/blob/v3.2.0/Release/3.2.0.md) for behaviour changes and limitations.

## Version 3 migration

Since 3.0, Shell is required and framework creation is permitted only once per runtime. Models implementing `IRModel` must provide `Task Cleanup()`. Required actor lookups throw when registrations are missing or ambiguous.

Root submitters use `Execute(...)` for ordered batches; separate calls can overlap. Await child commands sequentially and check `ErrorOrAbort` before dependent work. Awaiting a child does not universally rethrow its recorded failure.

The 3.1 progress-hiding and command-scoped root-cancellation APIs remain available.

Existing repository samples await migration. Use the README quickstart as the current reference. Developers remain responsible for immutable payload ownership, actor thread safety and cooperative cleanup.

[Source and issue tracker](https://github.com/seanstardev/RMVC) · [MIT licence](https://github.com/seanstardev/RMVC/blob/v3.2.0/LICENSE)
