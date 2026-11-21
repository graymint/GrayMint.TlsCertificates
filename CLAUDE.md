# GrayMint.TlsCertificates

TLS certificates for the domains a host serves: ordered and renewed from an ACME authority by a renewal in one host,
kept in tables of its own on PostgreSQL or SQL Server, and served by Kestrel in any host. The protocol is
GrayMint.Common.Acme's; this module keeps and decides.
Read [docs/design.md](docs/design.md) before changing code: it says what the code holds, what the database holds,
and why each is where it is. Keep it, and the README, true in the same change that makes them wrong.

## Rules

1. **The repository is general.** Its code, docs, tests and commit messages name no other product, company or
   person. Examples use neutral names: a shop, a user, `example.com`.
2. **A guard comes with its test, written so it can't pass for another reason.** Whatever keeps the certificates
   right (the lease, a domain's form, the wait after a failure, a challenge's token and domain, what an error may
   say, the setup's checks) gets a test that checks exactly what it refuses, and that nothing was kept.
3. **One renewal at a time.** Every run takes the lease first, renews it before each order and gives it back at its
   end; nothing writes the certificates outside a run, but `RetryNow`, which only lifts a wait.
4. **A row is written by one statement.** A certificate and a failure never go out in one save of tracked rows:
   each write is an `ExecuteUpdate` of what it changes.
5. **Tests run on a real PostgreSQL, a real SQL Server and a real ACME authority.** `docker compose up -d`, then
   `dotnet test`. Every case that touches the database runs on both (`[EveryDatabase]`), each in a database of its
   own, made as the setup makes it; the authority's tests run on Pebble. Run them after every change; a change isn't
   done until they pass.
6. **One model for both databases.** Whatever EF can say goes through EF. The package names no database provider:
   the host names its own.
7. **Fail loudly.** A setting that is wrong stops the registration; a database without the tables, or with tables of
   another version, stops the start; so does a host without its domains or protector. The renewal's job logs a run
   that fails and runs the next: a stopped renewal lets every certificate expire without a word.
8. **.NET 10**, pinned by `global.json`.
9. **A change to the tables raises their version.** The start takes tables of `TlsCertificatesSchema.Version` alone.
   A change to the model raises it, and gives the setup a step that takes tables of the version before to it, with a
   test that runs the step on such tables, in the same change.

## Code

- One type per file, whatever its visibility. `Persistence` is internal throughout: a host sees the services, the
  options, the classes they take and give, the two interfaces it implements, `TlsDomain`, `TlsCertificatesSchema` and
  the registration, and nothing else. A service's constructor is internal, so only the registration makes one.
- Every public type and member has an XML doc (`///`): what a caller gets, and what refuses it. The build fails
  without one.
- Every time is UTC, written as `TimeProvider` gives it and read back as UTC; a test moves the clock.
- Fields and state at the top of a class. No `!` to silence a null: check it, or throw with what is wrong.
- A method that waits is async and takes a required `CancellationToken`, which it passes on. A method hands back one
  named type, never a tuple; a list is an `IReadOnlyList<T>` or `IReadOnlyCollection<T>`.
- A comment says why the code is there, or which tempting change would break what. No history, and nothing the code
  already says.
- No top-level statements. Nothing temporary in the repository: scratch goes to the system's temp folder.

## Publishing

Every push to `main` publishes the `GrayMint.TlsCertificates` package to nuget.org, so `main` only gets what is ready
to ship. CI builds, runs the tests on the services of `compose.yaml`, raises the version in `pub/PubVersion.json` and
`src/Directory.Build.props` with a commit of its own, and pushes the package; a failed test stops it before the
version moves. Never change the version by hand. `publish_nugets.yml` is a thin wrapper around the shared workflow of
`graymint/GrayMint.Common`, and nuget.org takes the package only from that file in this repository (a trusted
publishing policy, made on nuget.org before the first publish), so renaming the file or the repository stops
publishing.

The package depends on GrayMint.Common.Acme. While both change together, the project references it by its path,
marked `<!-- DEV -->`; before a push, it references the published package instead, as CI has no sibling repository.

The README is the package's page on nuget.org.
