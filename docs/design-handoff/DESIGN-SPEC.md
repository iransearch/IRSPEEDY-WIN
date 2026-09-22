# IRSPEEDY — Windows 11 Implementation Handoff

Source: Cowork "Design" canvas — 4 approved screens (Server List, Login, Connected Status, Settings).
Target: WPF / .NET Framework desktop app, `iransearch/IRSPEEDY-WIN`.
Language/direction: Persian, **RTL** (`FlowDirection="RightToLeft"`), header wordmark "IRSPEEDY" stays **LTR**.

This package replaces any earlier handoff export — it reflects the final, approved state of all 4 screens, including the opaque (non-transparent) window background, the enlarged Connected/Login logos, the 10-country server list, and the new Settings screen.

---

## 1. Global design tokens

### 1.1 Window

- Size: **420 × 700** DIP, all 4 screens.
- Corner radius: **20px**, all corners.
- Background: **fully opaque** — `linear-gradient(180deg, #FFFFFF 0%, #F8FAFC 100%)`. No blur / Mica / Acrylic / transparency anywhere in the app chrome — this was an explicit design decision (earlier translucent-glass mockups were reverted).
- Drop shadow (mockup only, not required in the real window): `0 40px 80px -24px rgba(23,35,64,0.35)`.
- No native OS titlebar — custom header with logo + minimize/close (see 1.2).

### 1.2 Standard header (Server List, Login, Connected)

- Height 56px, padding `0 18px 0 12px`, RTL row: **logo → wordmark+version (flex-grow) → [screen-specific icon buttons] → divider → minimize → close**.
- Logo: `assets/logo/logo-header.png` (or `irspeedy.ico`/SVG at build time), ~22–24px wide, aspect-locked (see asset manifest).
- Wordmark block is wrapped in an **LTR** sub-container so "IRSPEEDY" doesn't reverse inside the RTL row: `<span>IRSPEEDY</span> <span class="muted">v1.4.5.8</span>`, 15px / 700 weight, version 11px / 500 weight muted (`#9CA3B4`).
- Icon buttons: 30×30, radius 8, transparent, hover `rgba(17,24,39,0.06)`.
- Minimize icon: single horizontal line. Close icon: X (two diagonal lines). Both `#5B6478`, stroke-width 1.4.
- **Settings screen uses a different header** — see section 5.

### 1.3 Colors

| Token | Value | Usage |
|---|---|---|
| Accent | `#1E3A8A` (gradient top `#2447A8`) | Primary buttons, smart-location icon tile |
| Ping / success green | `#17A366` | Ping values, "اعتبار باقی‌مانده", connected badge |
| Connected dark green | `#0F7A4C` | — |
| Disconnect red | gradient `#DC2626 → #B91C1C` | Disconnect button, Settings close button (`#EF4444 → #DC2626`) |
| Toggle ON green | gradient `#22B37A → #159A63` | Settings toggles |
| Toggle OFF red | gradient `#F04A4A → #DC2626` | Settings toggles |
| Text primary | `#141B33` | Titles |
| Text row title | `#1B2033` | Server names, settings labels |
| Text muted | `#9CA3B4` / `#8A8FA3` | Secondary text, version tag |
| Borders | `#E1E4EC` | Fields, dividers, cards |
| Icon stroke | `#5B6478` | Line icons |

Full token list with hex values is in `wpf/IrspeedyTheme.xaml`.

### 1.4 Typography

Font: **Vazirmatn** (Google Fonts for the web mockup; bundle the `.ttf`/`.otf` locally for WPF — no network font loading). Weights used: 400, 500, 600, 700, 800.

Numeric values that should read in Persian (Arabic-Indic digits) in the UI: ping values, dates, the connection-duration timer, remaining-days. IP addresses and the app version stay in Latin digits, wrapped `dir="ltr"`.

---

## 2. Screen 1 — Server List (`Persian.dc.html` / "Main")

Header as in 1.2, with two extra icon buttons before the divider: **gear/settings** (opens the Settings screen) and **shield/security**.

Content (padding `4px 18px 18px`, `gap: 12px`):

1. **Search field** — 44px height, radius 12, border `#E1E4EC`, placeholder "جستجوی سرورها", search icon left of text (RTL: icon appears on the right of the input visually).
2. **Smart Location card** — 60px height, radius 14, border `#C9DBFB`, background gradient `#EFF5FF → #E6F0FE`. 34×34 accent-colored icon tile (lightning-bolt glyph) + title "موقعیت هوشمند" (14px/700, `#142057`) + subtitle "سریع‌ترین سرور، انتخاب خودکار" (12px, `#5B6BA8`).
3. **Server list** — scrollable, flex-grow, row height auto (~62px incl. padding), radius 12 per row, hover `#F3F5F9`. Each row: 38px circular flag → label (14.5px/600) → ping value (13px/700, green) → 20px empty radio circle (border `#D5D9E3`, 2px).

**10 servers, in order** (flag / label / ping):

| Flag | Label | Ping |
|---|---|---|
| 🇩🇪 Germany | فرانکفورت - آلمان | ۳۸ میلی‌ثانیه |
| 🇳🇱 Netherlands | آمستردام - هلند | ۴۴ میلی‌ثانیه |
| 🇹🇷 Turkey | استانبول - ترکیه | ۵۱ میلی‌ثانیه |
| 🇫🇷 France | پاریس - فرانسه | ۴۷ میلی‌ثانیه |
| 🇬🇧 UK | لندن - انگلستان | ۵۵ میلی‌ثانیه |
| 🇺🇸 USA | نیویورک - آمریکا | ۶۲ میلی‌ثانیه |
| 🇨🇦 Canada | تورنتو - کانادا | ۶۸ میلی‌ثانیه |
| 🇦🇪 UAE | دبی - امارات | ۲۹ میلی‌ثانیه |
| 🇸🇬 Singapore | سنگاپور - سنگاپور | ۸۲ میلی‌ثانیه |
| 🇯🇵 Japan | توکیو - ژاپن | ۷۵ میلی‌ثانیه |

Flag PNGs (76×76 circular, high-res source vectors also embeddable) are in `assets/flags/`.

4. **Connect button** — 56px height, radius 14, accent gradient, white text "اتصال" + left-pointing chevron/arrow icon, box-shadow `0 10px 22px -8px rgba(30,58,138,0.5)`. Bottom edge of content area sits **18px** from the window's bottom edge.

---

## 3. Screen 2 — Login (`Login.dc.html`)

Header: same as 1.2 but **no gear/shield icons** — just logo, "IRSPEEDY" (no version tag), minimize, close.

Content (padding `6px 32px 16px`, `gap: 14px`):

1. **Brand block**, centered:
   - Badge: **200×200**, radius 52, soft white/blue gradient fill, subtle shadow — enlarged to match the Connected screen's hero scale (was 96×96 in an earlier draft; explicitly resized up).
   - Logo image inside badge: **100×135** (aspect-locked from the 720×968 source), `object-fit: contain`.
   - Wordmark "IRSPEEDY" (English, LTR) — 20px/800.
   - Tagline "دسترسی امن و پرسرعت به اینترنت" — 13px, `#6B7280`.
2. **Form**, in order:
   - "نام کاربری" (username) — text field, user icon, height 48px, radius 12.
   - "رمز عبور" (password) — password field, lock icon, eye-toggle icon, height 48px, radius 12.
   - No "forgot password" link (explicitly removed).
3. **Actions**:
   - "ورود" (Sign in) button — 52px height, radius 14, accent gradient, left arrow icon.
   - "یا" divider.
   - "حساب کاربری ندارید؟ ثبت‌نام" (sign-up prompt) — centered, link in accent color.

---

## 4. Screen 3 — Connected Status (`Connected.dc.html`)

Header: same minimal header as Login — logo, "IRSPEEDY" + version, minimize, close. **No gear/shield icons here** (explicitly removed — those live on the Server List / Settings screens only).

Content (padding `4px 22px 18px`, `gap: 8px`) — bottom padding **18px**, matching the Server List screen's Connect-button clearance so both primary actions sit the same distance from the window's bottom edge:

1. **Connection hero** (centered):
   - Outer glow ring: 336×336, radial green glow (`rgba(23,163,102,0.16)` → transparent).
   - Thin ring stroke at 26px inset, `rgba(23,163,102,0.25)`, 2px.
   - Inner circle: **248×248**, white→`#F3FBF7` gradient, soft green-tinted shadow.
   - Logo inside: **124×168** (large — matches the badge treatment used on the Login screen).
   - Check badge: 60px circle, solid green `#17A366`, 6px white border, white checkmark — anchored bottom-center, overlapping the inner circle.
2. **Server card** — 50px height, radius 14, light blue background/border (same visual language as Smart Location card). Flag circle (30px) + server name + "ping · موقعیت هوشمند" subtitle + icon-only **swap/change server** button (32×32, radius 9, 4-arrow exchange glyph in accent blue — replaces an earlier text link "تغییر").
3. **Account / subscription info card** — white card, radius 12, border `#E1E4EC`, 3 rows separated by 1px dividers:
   - مدت زمان اتصال (connection duration) — icon + label, value **۰۰:۱۲:۴۷** in Persian digits, `dir="ltr"`, bold.
   - تاریخ انقضا (expiry date) — value **۱۴۰۴/۱۰/۱۵**, Persian digits.
   - اعتبار باقی‌مانده (remaining credit) — value **۱۸ روز**, green, bold.
4. **IP row** — centered, muted: "آی‌پی جدید شما: 185.23.44.109" (IP stays Latin digits, `dir="ltr"`).
5. **Disconnect button** — 46px height, radius 14, red gradient `#DC2626 → #B91C1C`, white text "قطع اتصال" + disconnect/power icon, `margin-top: auto` (pinned to the bottom of the content column, 18px from the window edge).

---

## 5. Screen 4 — Settings (`Settings.dc.html`)

A **modal-style screen**, not the standard app chrome:

- Header (64px): centered title "تنظیمات سرویس" (16.5px/800), **no logo, no minimize** — just a circular red close (X) button anchored to the trailing edge (30px diameter, red gradient `#EF4444 → #DC2626`).
- Content: 6 toggle rows, laid out with `justify-content: space-between` so they spread evenly across the full content height (no dead space above the confirm button). Row: 68px height, radius 16, white card, border `#E8EAF0`. Label on the trailing side (15px/700), a **pill toggle** on the leading side.
- **Toggle widget** (86×32 pill): a light-gray track containing a colored segment (green "ON" / red "OFF", flex-grow) and a white circular thumb (26px) with a chevron icon (‹ when ON, › when OFF). This is a custom two-state control, not a stock WPF `ToggleButton` — build it as a `UserControl` with a bound `bool IsOn` property; see `assets/icons/toggle-on.svg` / `toggle-off.svg` for the thumb glyphs.
- **Rows and default state**:

| Label | Default |
|---|---|
| وی پی ان سراسری | ON |
| پروکسی سیستمی | OFF |
| پروکسی‌فایر | OFF |
| پروکسی تلگرام، واتساپ | OFF |
| VOD/AI | ON |
| Game Mode | OFF |

- **Confirm button** — full width, 50px height, radius 14, light-gray fill `#EDEFF3`, border `#DCDFE6`, text "تایید" (`#3A4054`, 14.5px/700). Not accent-colored — visually secondary/neutral, matching the reference mock.

---

## 6. Asset manifest

```
assets/
  logo/
    irspeedy.ico          multi-res app icon (16/32/48/256)
    icon-16.png … icon-256.png
    logo-header.png        720×968 high-res source (shield+rocket mark)
  flags/
    germany.png, netherlands.png, turkey.png, france.png, uk.png,
    usa.png, canada.png, uae.png, singapore.png, japan.png
                            76×76 circular PNGs, one per server row
  icons/                   24×24 viewBox SVGs (WPF: import as Path/Viewbox,
                            or convert to a WPF Geometry resource)
    search.svg, lightning.svg, gear.svg, shield.svg,
    window-minimize.svg, window-close.svg, close-circle.svg,
    user.svg, lock.svg, eye.svg, arrow-left.svg, arrow-right.svg,
    swap-server.svg, clock.svg, calendar.svg, check.svg,
    power-disconnect.svg, toggle-on.svg, toggle-off.svg,
    download.svg, upload.svg
wpf/
  IrspeedyTheme.xaml       ResourceDictionary: colors, brushes, font sizes,
                            layout constants for all 4 screens
```

Notes:
- `gear.svg` and `swap-server.svg` are the final, approved icon shapes (the earlier spoke-style gear icon in a prior export is superseded — do not reuse it).
- All flag PNGs are pre-masked to circles; if you'd rather clip at render time in WPF, use an `EllipseGeometry` clip and the rectangular flag proportions noted in each SVG (viewBox `0 0 38 38`).

---

## 7. WPF integration notes

- Merge `wpf/IrspeedyTheme.xaml` into `App.xaml` (see comment header in that file).
- Bundle Vazirmatn font files under `/Fonts/Vazirmatn/` and reference via `pack://application:,,,/Fonts/#Vazirmatn`. Do not load fonts from Google Fonts at runtime.
- Set `FlowDirection="RightToLeft"` on each window root; wrap the LTR wordmark/IP/duration spans in a nested `FlowDirection="LeftToRight"` container so they don't visually reverse.
- Window background: bind to `WindowBackgroundBrush` (opaque gradient) — do not enable `AllowsTransparency` or apply a blur/acrylic effect; the approved design is fully opaque.
- Use `WindowChrome` (or a borderless `Window` with custom hit-testing) to implement the custom header's minimize/close buttons and window dragging, since there's no native titlebar.
- The Settings screen is a separate `Window` (or a modal `Page`/`UserControl` overlay) launched from the gear icon on the Server List header; its close (X) button returns to the previous screen rather than closing the app.
- Persian-digit formatting: format ping/duration/date/remaining-day values through a converter that maps `0-9` → `۰-۹` (U+06F0–U+06F9); keep IP addresses and the app version string in Latin digits.
