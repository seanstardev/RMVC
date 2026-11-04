# RMVC 3.0.0

**Readable, navigable, compiler-checked application workflows.**

RMVC is a small MVC framework for C# applications, targeting .NET Standard 2.0.
Commands coordinate models, proxies and mediators through explicit, typed calls.
Views own presentation, while an application's abstract `CommandBase` gives its
concrete commands a readable vocabulary for the capabilities they use.

```sh
dotnet add package seanstardev.RMVC --version 3.0.0
```

Start with the [complete counter quickstart and diagrams](https://github.com/seanstardev/RMVC/blob/v3.0.0/README.md).
It implements the model, mediator, view contract, command base, commands, facade
and Shell, with a runnable .NET 9 console host. The repository's README verification
builds and exercises both its single-command and composed-command journeys.

## Version 3 migration

This is a breaking release. Shell is required; framework creation is permitted
only once per runtime. Models implementing `IRModel` must provide `Task Cleanup()`.
Required actor lookups throw when registrations are missing or ambiguous.
Root submitters use `Execute(...)` for ordered batches; separate calls can overlap.
Awaited child commands continue sharing their root's error, cancellation and progress context.

See the [3.0.0 release notes](https://github.com/seanstardev/RMVC/blob/v3.0.0/RELEASE-NOTES-3.0.0.md)
for the main changes. Existing repository samples await migration; use the README
quickstart as the current reference. Developers remain responsible for actor and
model thread safety, immutable payload ownership and cooperative cleanup.

[Source and issue tracker](https://github.com/seanstardev/RMVC) · [MIT licence](https://github.com/seanstardev/RMVC/blob/v3.0.0/LICENSE)
