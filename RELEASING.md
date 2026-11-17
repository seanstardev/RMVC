# Releasing RMVC

## Confirm the checkout first

The first message to the maintainer for every release must ask:

> Please make sure the RMVC repository is up to date. Have you saved and committed
> all the changes you want in this release, and is this the checkout and branch
> you want me to release from?

Wait for confirmation before changing release files, committing, tagging or
publishing. Check the working tree, branch and remote. If there are pending
changes or the branch has diverged, show the maintainer what you found and agree
which commit to release. A clean working tree cannot confirm that editor buffers
have been saved or that changes from another checkout have been included.

## Present the plan and README

Before writing release files, outline the exact steps for this release in
order. Name the version, source branch, files to change, checks to run,
artifacts to build, tag, publishing destinations and verification steps.
Identify where the maintainer may need to sign in or submit an upload.

Show the proposed release README text in chat before creating or editing it.
Include the full proposed package README and any proposed changes to the
repository README. Let the maintainer review the text and plan before
proceeding.

## Prepare

1. Confirm the version and the exact framework changes going into it. Review
   public API changes against the previous tag and explain any migration in the
   release notes.
2. Check that the version is available on both GitHub and NuGet.org. Update
   `RMVC/RMVC.csproj`, `RMVC/README.NuGet.md` and
   `RELEASE-NOTES-<version>.md`. Match the package ID, version and tagged
   documentation links across all three.
3. Run the framework contract scenarios, the positive and negative
   [access checks](Tests/RMVC.AccessChecks/README.md), and the README quickstart
   verifier. Each framework scenario runs in a fresh process.
4. Commit the reviewed changes. Build and pack from that exact clean commit.
   Keep release artifacts in a version-specific output directory.

## Build and verify the bundle

Run from the repository root in PowerShell with the .NET 9 SDK and Python 3.
Set `$releaseVersion` to the version in the project file.

```powershell
$releaseCommit = git rev-parse HEAD
$releaseOutput = "artifacts/release-$releaseVersion"
dotnet pack RMVC/RMVC.csproj --configuration Release --output $releaseOutput `
    -p:ContinuousIntegrationBuild=true -p:RepositoryCommit=$releaseCommit
python Tests/RMVC.ReadmeChecks/verify.py --package "$releaseOutput/seanstardev.RMVC.$releaseVersion.nupkg"
```

Inspect the package manifest for the version, repository commit, README and
license. Extract `lib/netstandard2.0/RMVC.dll` from the package as the standalone
GitHub asset. Record SHA-256 hashes for both files.

## Publish

1. Push the release commit and its annotated `v<version>` tag. Verify that the
   remote tag resolves to the commit embedded in the package.
2. Publish the GitHub release using the tagged release notes. Attach `RMVC.dll`
   and the verified nupkg before publishing. GitHub creates the source archives.
   Use an existing authorised GitHub session or credential.
3. Publish the same nupkg to NuGet.org. Use a securely configured publishing
   credential when available. Otherwise open [NuGet Upload](https://www.nuget.org/packages/manage/upload),
   sign in, choose the verified nupkg, check the package preview and submit.
   The maintainer handles sign-in or MFA when prompted. Keep credentials out
   of chat, logs and repository files.
4. Verify the public GitHub assets against the recorded hashes. Wait for NuGet
   validation and indexing, then restore the exact version into a clean
   package cache. Compare the packaged DLL with the GitHub asset. Record the
   public links, release commit and verification results.

Reuse the existing artifact if validation is still processing. If a published
version needs a correction, prepare a new version.

## Close out

Report the verified GitHub release and NuGet package links. Then ask the
maintainer whether to update consuming projects to the newly published NuGet
version. Wait for their decision before changing project references.
