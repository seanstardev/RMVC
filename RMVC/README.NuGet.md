# RMVC 3.1.0

**Readable, navigable, compiler-checked application workflows.**

RMVC is a small MVC framework for C# applications, targeting .NET Standard 2.0.
Commands coordinate models, proxies and mediators through explicit, typed calls.
Views own presentation, while an application's abstract `CommandBase` gives its
concrete commands a readable vocabulary for the capabilities they use.

```sh
dotnet add package seanstardev.RMVC --version 3.1.0
```

Start with the [complete counter quickstart and diagrams](https://github.com/seanstardev/RMVC/blob/v3.1.0/README.md).
It implements the model, mediator, view contract, command base, commands, facade
and Shell, with a runnable .NET 9 console host. The repository's README verification
builds and exercises both its single-command and composed-command journeys.

## Version 3 migration

Since 3.0, Shell is required and framework creation is permitted
only once per runtime. Models implementing `IRModel` must provide `Task Cleanup()`.
Required actor lookups throw when registrations are missing or ambiguous.
Root submitters use `Execute(...)` for ordered batches; separate calls can overlap.
Awaited child commands continue sharing their root's error, cancellation and progress context.

In 3.1, commands can await `base.HideProgress()` and request cancellation with
`base.AbortRootCommands<TRootCommand>()`. `RFacade.AbortCommands<TFacade>()`
is now internal. `RFacade.AbortAllCommands()` remains public.

See the [3.1.0 release notes](https://github.com/seanstardev/RMVC/blob/v3.1.0/RELEASE-NOTES-3.1.0.md).
Existing repository samples await migration. Use the README quickstart as the
current reference. Developers remain responsible for actor and
model thread safety, immutable payload ownership and cooperative cleanup.

[Source and issue tracker](https://github.com/seanstardev/RMVC) · [MIT licence](https://github.com/seanstardev/RMVC/blob/v3.1.0/LICENSE)
