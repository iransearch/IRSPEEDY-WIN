# Four-screen implementation

Based on IRSPEEDY-design-handoff(3).zip. Uses the existing MainWindow content host
for login, server selection, and connection status. VGAURDServiceSetting remains
an owned modal and persists through RegHelper. Connection modes retain their
existing mutual exclusion, Game Mode forces TUN, and saved values override defaults.
The default for an unset VOD/AI preference is now on, as requested in the handoff.

The bundled Vazirmatn families use their actual embedded family names and directory.
All windows have opaque backgrounds; the connected hero glow is drawn on that
opaque surface. Logos and all ten named flag assets come from this handoff.
Other real backend countries keep the bundled ISO flag fallback. Demo countries,
pings, subscription dates and IP addresses are not substituted for live data.
The public IP remains unavailable (an em dash); signup requires signup-url.txt,
as before. Password visibility and existing authentication handlers are retained.

Header service actions use the new gear and shield geometry. Login and connection
headers have no service-action icons. Connection sharing, probing, and optional
base-service selection remain in the hero context menu; account password changes
are available in the wordmark context menu. Primary action bottom clearance is 18 DIP.

For the server list, text containers and physical columns use LTR layout with
right alignment; Persian content is a RTL Run. This avoids flipping text alignment
across mixed-direction FrameworkElement boundaries. Search is right aligned with
Persian input language; Unicode still shapes Persian input. Latency, duration,
expiry and remaining credit display Persian digits. The version and IP stay Latin.

Validation performed: XML parsing, resource/asset resolution, embedded font-family
inspection, named-event handler checks and layout dimension review. This Linux
workspace has no .NET SDK or WPF runtime, so compilation and actual Windows UI
verification are pending. On Windows verify all four views at 100%, 125%, 150%,
and 200% scaling, RTL text/right edges, password reveal, settings normalization
and persistence, and connection/disconnection using real accounts and servers.
