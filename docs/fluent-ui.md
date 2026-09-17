# IRSPEEDY Windows UI (1.4.5.8)

Implemented in the existing WPF/net48 application, based on the supplied three-screen SVG.
One resizable window hosts login, server selection and connected state; existing application
events determine the current page. The initial screen still requires login or a valid cached
account before showing real servers.

## Visual contract

- Default window: 500 x 840 DIPs, minimum 420 x 700. Flexible content columns, scrolling
  lists/forms, WindowChrome resize borders and text wrapping support smaller windows and DPI scaling.
- Accent #0066FF, success #00C853, disconnect #E53935; soft blue waves and translucent cards.
- Vazir v30.1.0 Regular/Medium/Bold embedded under Resources/Fonts; OFL license included.
- Windows Segoe Fluent Icons with Segoe MDL2 Assets fallback. Flags are an offline atlas
  rendered from flag-icons v7.2.3 (MIT license included), with country-code fallback.
- 160 ms fade/slide honors Windows client-area-animation preference. No rotating transition.
- The material is a WPF translucent/blurred-wave approximation, not native WinUI Acrylic.
  Native Windows 11 Acrylic and pixel-for-pixel WPF rendering have not been verified here.

## Functional behavior

Search filters the existing country/service models, preserving selection, country pools and
the existing test completion ordering. Keyboard-focusable buttons show a radio-style selection
indicator. Failed countries keep the existing availability rule. Service/protocol selectors
remain accessible in an expander. Ping values come from real test results, not reference samples.

Password visibility is a keyboard-accessible toggle; edits in the visible field are used on
submission. Recovery opens the configured support URL when available, or explains how to contact
the account seller. There is no password reset API in the current service contract.

Change server disconnects via the existing handler and returns to the list on the disconnect
event. Connected status does not claim encryption for an unverified protocol. Cities are shown
only if included in the server's existing display name. Duration uses positive elapsed time.

IP display uses an optional HTTPS request to https://api.ipify.org, with a four-second timeout,
cancellation when leaving a page, and no IP logging. The pre-connection IP is sampled only before
this app has assigned a current service; it is a snapshot, not proof of a physical ISP address
(another VPN can affect it). Active IP uses the selected service's HTTP proxy when available;
otherwise it follows system routing (e.g. TUN). An unavailable measurement is labelled, never
replaced with a sample value. These requests do not gate login or connection.

## Windows verification

Use the existing builder to prepare Files.zip and make a fresh Release single-file build.
Check the version header and footer show 1.4.5.8. Test before and after SmartAssembly:

1. Launch with/without saved credentials; login success/failure and loading cancellation.
2. Tab through username, password toggle, remember checkbox, recovery and login. Edit a
   visible password, hide it, and verify the submitted value stays synchronized.
3. Search Persian country names and Latin codes; clear search; select Smart and a tested
   country by mouse and keyboard. Let results arrive and verify selection survives reordering.
4. Expand service/protocol options, connect, fail a connection, disconnect and change server.
5. Check real ping/duration, missing-data labels and IP lookup failure without delaying controls.
6. Resize and test 100/150/200% scaling, long country names and disabled system animations.
7. Check tray restore/minimize, settings, logout, connection test and sharing actions.

Source XML/C# syntax, resource paths, event-handler references, font metadata and flag atlas
were checked locally. A Windows WPF build and visual/runtime verification are still required.
