# Residential checks

Run without external network access or credentials (HTTPS tests use loopback):

```sh
python tests/ResidentialChecks/run.py "$DOTNET" "$CSC" "$NET8_REFS" "$NET48_REFS"
python tests/ServerListTransitionChecks/run.py "$DOTNET" "$CSC" "$NET8_REFS"
python tests/ServerListTransitionChecks/bindings.py
python tests/ServerListTransitionChecks/cycle_wiring.py
```

`DOTNET` is a .NET 8 host, `CSC` is its Roslyn `csc.dll`, `NET8_REFS` is
`Microsoft.NETCore.App.Ref/.../ref/net8.0`, and `NET48_REFS` is the reference
assembly directory from `Microsoft.NETFramework.ReferenceAssemblies.net48`.
No NuGet restore is needed by these commands. All generated assemblies are
temporary. The .NET 4.8 compile uses actual WPF references; existing service
boundaries are inert doubles, not substitute production implementations.
The cache checks execute production `ServerCheckCache` with the .NET SDK's bundled
`Newtonsoft.Json.dll`, copied next to the temporary test executable. They cover
independent scopes, stopped queue restart, cancellation rollback, fragment-only
metadata changes and migration of previous persisted fingerprints.

The HTTPS fixture creates a temporary CA in the current test user's Root store,
serves a local gzip/BOM subscription and removes that CA in `finally`. It never
disables TLS validation. Use an isolated test user/container as with other local
certificate fixtures. It checks the production GET-to-country-publication path
and verifies cancellation drains the socket/server task.

See `docs/windows-residential.md` for scope and required Windows visual/BAML,
accessibility, live transport and lifecycle smoke tests.

