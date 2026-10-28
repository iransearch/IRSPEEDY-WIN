# Residential checks

Run without real network access or credentials:

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

See `docs/windows-residential.md` for scope and required Windows visual/BAML,
accessibility, live transport and lifecycle smoke tests.
