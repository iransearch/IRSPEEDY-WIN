# Server-list handoff implementation

The supplied developer guide, HTML, screenshots and logo are retained verbatim in this folder. Screenshots are **design references**, not captures of the Windows application.

## Mapping

| Reference | WPF implementation |
| --- | --- |
| Main window/header | `IRSpeedyVPN/MainWindow.xaml`; 420 × 700 DIP, 56-DIP header, original logo, spaced header actions |
| Search/connect | `IRSpeedyVPN/UserControls/UCServerList.xaml`; fixed outside the scroller |
| Smart card/server cards | `IRSpeedyVPN/Components/ServerListControl/ServerCountryPicker.xaml`; 68-DIP smart card, 52-DIP rows, 8-DIP gaps, 14-DIP corners |
| Moving border light | `ServerCountryPicker.Motion.cs`; rounded perimeter, 14% dash, 1.6 seconds per lap; removed when hidden, unloaded or minimized |
| SVG flag shapes | `IRSpeedyVPN/Themes/ServerListFlags.xaml`; original geometry with circular clips, plus existing ISO fallback |
| Persian type | Existing embedded static Vazirmatn weights; 14 SemiBold titles, 12.5 Bold latency |

The search field's CSS content height is 44 plus two 1-pixel borders, so its WPF outer height is 46. Server cards similarly have a 30-DIP flag plus 20-DIP vertical padding and two 1-DIP borders. Shadows are on the background layer, leaving text outside the effect.

The physical list frame is LTR, with a dedicated 8-DIP scrollbar column. The scroller extends 4 DIP into the left outer margin: the 6-DIP rail ends 2 DIP before the cards, which start 4 DIP inward from the search field. Text is right-aligned with RTL runs; latency remains LTR. The rail never occupies the latency column.

Selection uses whole-card buttons (also keyboard accessible), retaining the existing single-selection and service-pool callbacks. Search resets the scroll position and shows an empty-result message. Backend names, live latency, failed-test availability and fastest-first ordering remain authoritative; reference cities and latency values are not injected into production data. Numeric latency continues to calculate the same three color bands in `GroupItem.SetSignalColor` for both text and the new dot.

## DPI clarification

The new main-window contract is **420 × 700 logical DIPs**. At 125% scaling this normally corresponds to **525 × 875 physical pixels**, excluding capture-dependent chrome/shadow. PerMonitorV2 prevents bitmap scaling; it does not make a logical DIP equal to a physical pixel at every monitor scale. The packaged references are rendered at 2× (840 × 1400). The main window hosts login/connecting/connected too, so those views also use the corrected physical window size. Separate settings dialogs retain their existing sizing.

## Verification

- `python tools/validate-ui-handoff.py`: resource names, handlers, property-element ordering, sizing and existing UI contracts.
- Cross-compiled the production picker XAML, picker C# and motion, server-list XAML, main-window XAML, header-rendering method and both resource dictionaries using the WPF compiler with C# 7.3. The isolated check uses stubs for unrelated services, page handlers and Transitionals; it is not a complete .NET Framework application build.
- Windows runtime rendering remains to be checked at 100%, 125% and 150%: compare top/bottom list references, keyboard selection, no-result search, hover, and minimizing/restoring the animated smart card. A Windows executable or runtime screenshot was not produced in this Linux environment.
