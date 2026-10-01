# Application appearance

The moon/sun button in the login header, immediately beside the small logo,
changes the entire application palette. It is owned by `MainWindow`, outside
both the page transition and the service-only header actions. Navigation shows
it on login (including return after logout), and hides it on other pages. It is
disabled while the login form is disabled. The sun denotes light mode and the
moon denotes dark mode; the tooltip describes the switch to the other mode.
The icons are vector paths, independent of installed fonts.
Light remains the default. The last choice is stored per Windows user at
`HKCU\Software\IRSpeedyVPN\Appearance`, value `Theme` (`Light` or `Dark`).
It is restored immediately after `App.InitializeComponent`, before constructing
the main window or any cached page. Logging out does not reset it.

`Palette.Light.xaml` and `Palette.Dark.xaml` supply the same color keys.
Shared brushes and view properties use dynamic resources. Connection methods
and split tunneling keep their local styles, but their brushes consume the same
application palette. Theme changes replace one dictionary without rebuilding
pages or touching connection state. All resources compile into the existing
single-file build; no separate theme files need to be distributed.

Source checks: `python tests/ThemeChecks/run.py`.
Native WPF checks after a Windows build:

```powershell
powershell.exe -NoProfile -STA -File tests/ThemeChecks/run-wpf.ps1 -AppPath .\IRSpeedyVPN\bin\Release\net48\IRSpeedyVPN.exe
```

The native check loads the built views without showing them or initializing VPN
services. It checks cached and newly created controls, nested dictionaries,
repeated switching and input preservation, without saving a theme preference.
The Python check verifies XML, resource types/references, palette coverage and
dark text contrast; it does not substitute for a Windows render check.

Visual checks on Windows: switch both ways on login, restart in dark mode,
then inspect loading, servers/search, connecting, connected, traffic/reset,
settings, password fields, connection methods, split tunneling, sharing and
popups. Confirm the 420×700 layout, original rocket/flame, flags, QR readability
and drawer animation are retained.
