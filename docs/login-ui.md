# IRSpeedy login UI — 1.4.5.8

The production WPF/net48 window now uses the requested 630 × 920 layout, with a
420 × 680 minimum. The 628 × 834 inner design surface scales down uniformly,
while the 56-DIP title bar and 28-DIP status bar remain usable at minimum size.
The outer border accounts for the two remaining DIPs. A bottom-right resize
handle replaces native resize chrome; WindowStyle=None and AllowsTransparency=True.

The shared window background is #EBF0F8. The login card has 32-DIP horizontal
margins and padding, 16-DIP corners and a 24-DIP / 8% black shadow. Embedded
Vazir Regular/Medium/Bold are reused. The main bundled shield/rocket/heart logo
is 96 × 96. The title-bar button uses the requested AT monogram. Latin branding
uses individual glyphs with 2-DIP spacing because WPF TextBlock has no
letter-spacing property. Colors and login-specific templates are isolated in
Themes/LoginTheme.xaml; existing service/card styles keep their own accent.

## Wiring

- UCLogin owns a LoginViewModel and explicitly receives IAuthService through
  ConfigureAuthentication. MainWindow injects DelegateAuthService wrapping the
  existing Login/ProcessInfo workflow. Successful login shows the existing
  server-selection shell. Required updates retain priority over shell navigation.
- Remembered sessions, expiry/renewal, device limits and API endpoint/time-budget
  rules still use the existing production flow. Manual login uses a button
  spinner, with disabled/dimmed inputs, rather than the general loading overlay.
- StubAuthService is opt-in for a preview/test: demo / demo123 succeeds after
  800 ms. Nothing in production startup selects the stub. The default constructor
  fails closed until MainWindow installs the real service.
- Recovery opens the existing configured support URL or displays the seller
  contact instruction. The API does not expose a dedicated password-reset URL.
- The close caption button preserves the existing close-to-tray behavior (and
  exits when an update is mandatory); mouse, Enter and Space use its Click handler.

## Password and input state

The model owns a SecureString and copies values on assignment/read. Plaintext is
materialized only for the requested string-based auth interface and for explicit
password display; temporary unmanaged buffers are zeroed. The legacy account
workflow still has its existing string credential/cache contract. This change
does not claim to eliminate all plaintext from that older subsystem.

Only one of PasswordBox/TextBox is visible and populated at a time. The visible
TextBox disables undo history. Starting login hides that field; successful login
clears both view and model password. Login cannot be submitted twice while pending.
Validation is per field; a network failure is a general inline error and is not
mislabelled as an invalid username/password.

Tab order: username → password (masked or visible) → eye button → remember →
recovery → login. Enter in either credential field executes LoginCommand once.
Enter on recovery/toggle retains the control's own action. Inputs and buttons
have automation names; the eye button also has help text and an updated name.
Inline errors use an assertive live region and raise LiveRegionChanged after
layout. Keyboard focus rings surround buttons/check box; invalid fields get
red borders, and the first invalid input receives focus.

## Checks

Run the portable behavior checks with a .NET 8 SDK:

```sh
dotnet run --project tests/LoginViewModelChecks/LoginViewModelChecks.csproj
```

Locally verified:
- 17 executable assertions: independent empty-field validation, SecureString
  ownership, toggle/loading state, duplicate submission, success/navigation,
  password clearing, rejection, recovery and safe exception messages.
- Login view code-behind, authentication types and view model compile against
  .NET Framework 4.8 reference assemblies with C# 7.3. A temporary harness supplies
  the generated XAML fields and the unchanged application service dependencies.
- XAML parses; 716 XML attributes resolve against the net48/Transitionals API;
  resource keys and event handlers resolve. This is not a full WPF markup build.

Use the existing Windows builder for the complete single-file Release build.
On Windows verify the login at default/minimum size and 100/150/200% display
scaling, keyboard-only navigation, Narrator error announcement, password edits
across visibility toggles, mouse/keyboard hover/focus/loading, recovery,
remembered login, expired accounts, device limits, logout and tray restore.
Repeat the login smoke check after SmartAssembly. The bound LoginViewModel has
Obfuscation(Exclude=true, ApplyToMembers=true) to preserve binding property names.

A native WPF screenshot/render and full production build have not been run in
this Linux workspace; no pixel-perfect runtime verification is claimed.
