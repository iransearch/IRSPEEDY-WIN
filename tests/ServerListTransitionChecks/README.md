# Country-row transitions and protected-build enum conversion

Run the platform-independent checks with .NET 8:

```text
python tests/ServerListTransitionChecks/run.py
python tests/ServerListTransitionChecks/bindings.py
python tests/ServerListTransitionChecks/contracts.py
```

The contract check also reads the 1.4.20 row artwork from git commit `823c842`.
For native Windows BAML/layout/animation checks, use the existing
`run-wpf.ps1 -AppPath <unobfuscated IRSpeedyVPN.exe>` in a Windows PowerShell STA
session. The native check has not been run in the Linux environment.

## 1.4.32 startup crash regression

The customer 1.4.31 log records XamlParseException during deferred row-template
loading: EnumConverter cannot find `Selection` on an enum with an obfuscated
name. `ServerCountryPicker.xaml` supplied five enum member names as strings.
The release obfuscator could rename those fields without updating the strings.

The row template now supplies the stable numeric ServerRowPart IDs, and the
enum declares all IDs explicitly. This removes name lookup without depending
on a particular obfuscator's exclusion settings or disabling protection.
The IDs retain the existing slot-array mapping: Signal=0, Badge=1, Name=2,
Flag=3, Selection=4; None remains -1.

`run.py` reads the actual attached-property values from the production XAML.
It emits an enum with the same numeric values and different type/member names,
reproduces the original Selection/EnumConverter error, and verifies that every
production XAML token converts successfully against that renamed enum. It also
runs the existing production planner/timeline tests. The regression fails with
the old XAML and passes with the corrected template.

Linux validation uses Roslyn C# 7.3 and .NET 8 reference/runtime assemblies.
This verifies the converter failure mode, stable IDs, presentation contracts
and state-machine behavior. It does not execute SmartAssembly or the final
protected Windows single-file application.
