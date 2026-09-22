# Server list RTL and assets

The list uses explicit physical columns (left to right: selector, latency,
country name, flag). Country text has RTL flow and right alignment; latency
has LTR flow. The search grid places the icon on the right, with an RTL input
and a right-aligned placeholder. This avoids relying on inherited flow through
the transition host and control templates.

All main design font aliases use the supplied embedded B Yekan+ Bold font.
The header uses the existing 720 x 968 logo source instead of the 32 px icon,
with high-quality bitmap scaling and layout rounding for display scaling.

The original five flags remain. Additional two-letter codes resolve to local
114 x 114 PNGs in Resources/Irspeedy/Flags/Iso. These are rendered from the 1x1
SVG set in https://github.com/lipis/flag-icons, under its MIT license, included
beside the images and embedded in the executable. No runtime network request
is required. Unknown codes retain the initials badge.

Static checks cover XML/resource references, embedded assets, font family,
and the Italy/UAE/Ukraine/USA flags visible as missing in the screenshot.
Windows build and visual validation at 100%, 150% and 200% DPI remain necessary.
