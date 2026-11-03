# Application-facing access checks

These fixtures are separate consuming assemblies. The default probe must compile:

```text
dotnet build RMVC.AccessChecks.csproj
```

The following builds must fail with C# compiler errors, not restore/infrastructure errors:

```text
dotnet build RMVC.AccessChecks.csproj -p:Probe=InheritedAccess
dotnet build RMVC.AccessChecks.csproj -p:Probe=AlreadyInternal
dotnet build RMVC.AccessChecks.csproj -p:Probe=InstanceAbort
```

InheritedAccess contains 15 now-forbidden calls through facade/model/mediator/bridge
inheritance. AlreadyInternal contains three direct internal-resolver calls. InstanceAbort
verifies that public AbortAllCommands must be called statically. EssentialUsage verifies
registration/progress factories, root Execute, command lookup/navigation and public host
operations. These are compile checks, not runnable examples or framework features.
Negative sources are excluded from default compilation. Use a separate artifacts path
when automating checks. Samples and Datinate are outside this migration.
