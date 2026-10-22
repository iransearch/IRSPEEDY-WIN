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

The row template supplies stable numeric ServerRowPart IDs, and the enum
declares all IDs explicitly. In 1.4.33, the attached Part dependency property,
getter and setter expose int. This is required because WPF's markup compiler
rejects numeric literals for enum-typed properties (MC3032 in the 1.4.32 build).
FindGroups uses the integer index with bounds checks. This removes name lookup without depending
on a particular obfuscator's exclusion settings or disabling protection.
The IDs retain the existing slot-array mapping: Signal=0, Badge=1, Name=2,
Flag=3, Selection=4; None remains -1.

`run.py` reads the actual attached-property values from the production XAML.
It emits an enum with different type/member names and reproduces the original
Selection/EnumConverter error. It compiles the actual ServerCometPart declaration
with inert WPF dependency-property doubles, asserts matching integer metadata
and accessor signatures, and converts/round-trips every production XAML token
with the actual property type's converter. The new integer-type guard fails
against 1.4.32. It also runs the existing production planner/timeline tests.

Linux validation uses Roslyn C# 7.3 and .NET 8 reference/runtime assemblies.
This verifies the converter failure mode, stable IDs, presentation contracts
and state-machine behavior. It does not execute SmartAssembly or the final
protected Windows single-file application.
