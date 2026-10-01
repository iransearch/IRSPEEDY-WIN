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

`Palette.Light.xaml` and `Palette.Dark.xaml` supply the same color and brush keys.
Each palette resolves its brushes against its own colors with `StaticResource`;
views select those complete brushes with `DynamicResource`. Switching only the
colors beneath shared live brushes previously left a mixture of dark text and
light backgrounds on rendered/cached views. No page-local dictionary defines
theme brushes, including connection methods, split tunneling and native menus.
The few view-model properties returning a cached solid brush use `GetLiveBrush`;
the theme manager updates these stable copies after replacing the dictionary.
Theme changes do not rebuild pages or touch connection state. All resources compile into the existing
single-file build; no separate theme files need to be distributed.

Source checks: `python tests/ThemeChecks/run.py`.
Native WPF checks after a Windows build:

```powershell
powershell.exe -NoProfile -STA -File tests/ThemeChecks/run-wpf.ps1 -AppPath .\IRSpeedyVPN\bin\Release\net48\IRSpeedyVPN.exe
```

The native check loads the built views without showing them or initializing VPN
services. It checks cached and newly created controls, nested dictionaries,
repeated switching and input preservation, without saving a theme preference.
It also renders cached/new palette consumers and checks paired foreground and
background colors in cold dark startup, dark mode and return to light mode.
The Python check verifies XML, resource types/references, palette coverage and
dark text contrast; it does not substitute for a Windows render check.

Visual checks on Windows: switch both ways on login, restart in dark mode,
then inspect loading, servers/search, connecting, connected, traffic/reset,
settings, password fields, connection methods, split tunneling, sharing and
popups. Confirm the 420×700 layout, original rocket/flame, flags, QR readability
and drawer animation are retained.
