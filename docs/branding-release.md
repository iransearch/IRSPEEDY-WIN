# Branding release 1.4.5.8

The UI uses Resources/Logo-v2.png; the executable, window and tray use Logo-v2.ico.
Both resources are compiled into the executable. No loose image files are needed.
Resources/Logo.svg retains the original artwork.

The checked-in SmartAssembly project now reads bin/Release/net48/IRSpeedyVPN.exe
and writes bin/Release/net48/Obfuscated/IRSpeedyVPN.exe. It previously read Debug,
even though its configuration was named Release. If using a separate SmartAssembly
project, check its input path too.

From a Visual Studio Developer Command Prompt at the repository root:

```bat
msbuild IRSpeedyVPN\IRSpeedyVPN.csproj /restore /t:Rebuild /p:Configuration=Release /p:UseCosturaSingleFile=true
```

Close all running app instances before launching the new executable. The single-instance
mechanism otherwise brings the existing process to the foreground.
Confirm 1.4.5.8 both in the app header and in the executable's file properties.
Check the logo inside the app, taskbar, tray and Explorer before and after SmartAssembly.
Use the newly rebuilt Release executable as the protection input; do not reuse an old dist file.
Preserve the existing packaging workflow and required configuration/runtime handling.
Resource renaming does not guarantee Explorer's icon cache is refreshed.

Windows rebuild, SmartAssembly processing and visual runtime verification must be run
on Windows; they were not performed in the editing environment.
