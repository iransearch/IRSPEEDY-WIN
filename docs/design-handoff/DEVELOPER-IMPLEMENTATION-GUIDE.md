# IRSPEEDY — Developer Implementation Guide (WPF / .NET Framework)

This guide is written for the engineer implementing the 4 approved screens (Server List, Login, Connected Status, Settings) in `iransearch/IRSPEEDY-WIN`. It assumes you've already opened `DESIGN-SPEC.md` for the visual reference and have `assets/` + `wpf/IrspeedyTheme.xaml` from this same handoff package. This document tells you *how to wire it up*, step by step, with working XAML/C# you can drop in and adapt.

---

## 0. Prerequisites & project setup

1. **Target framework**: confirm the project is .NET Framework 4.7.2+ (WPF). If it's older, upgrade — `WindowChrome` and modern XAML features assume 4.7.2+.
2. **NuGet packages** (optional but recommended — pick one, don't mix):
   - `WPF-UI` or `ModernWpfUI` if you want native Mica/Acrylic later — **not required now**, since the approved design is fully opaque (see §2).
   - None of the 4 screens need a third-party control library; everything below is plain WPF + a couple of small custom `UserControl`s.
3. **Copy assets into the project**:
   ```
   /Assets/Logo/          ← from handoff assets/logo/
   /Assets/Flags/          ← from handoff assets/flags/
   /Assets/Icons/          ← from handoff assets/icons/ (SVG — see §1.3)
   /Fonts/Vazirmatn/        ← Vazirmatn .ttf/.otf files (Regular, Medium, SemiBold, Bold, ExtraBold)
   /Themes/IrspeedyTheme.xaml   ← from handoff wpf/
   ```
   Mark the font files and PNGs as **Build Action: Resource** (not "Content", unless you specifically want them loose next to the .exe).
4. **Merge the theme dictionary** in `App.xaml`:
   ```xml
   <Application x:Class="Irspeedy.App" ...>
     <Application.Resources>
       <ResourceDictionary>
         <ResourceDictionary.MergedDictionaries>
           <ResourceDictionary Source="Themes/IrspeedyTheme.xaml"/>
         </ResourceDictionary.MergedDictionaries>
       </ResourceDictionary>
     </Application.Resources>
   </Application>
   ```

---

## 1. Shared infrastructure (build this once, reuse on all 4 windows)

### 1.1 Borderless, rounded, draggable window base

None of the 4 screens use the native Windows titlebar. Create a base window style so you don't repeat this 4 times.

```xml
<!-- Themes/WindowBase.xaml -->
<Style x:Key="IrspeedyWindowStyle" TargetType="Window">
  <Setter Property="WindowStyle" Value="None"/>
  <Setter Property="AllowsTransparency" Value="False"/>  <!-- opaque per design -->
  <Setter Property="Background" Value="{StaticResource WindowBackgroundBrush}"/>
  <Setter Property="ResizeMode" Value="NoResize"/>
  <Setter Property="Width" Value="{StaticResource WindowWidth}"/>
  <Setter Property="Height" Value="{StaticResource WindowHeight}"/>
  <Setter Property="WindowStartupLocation" Value="CenterScreen"/>
  <Setter Property="FlowDirection" Value="RightToLeft"/>
  <Setter Property="FontFamily" Value="{StaticResource AppFontFamily}"/>
  <Setter Property="WindowChrome.WindowChrome">
    <Setter.Value>
      <WindowChrome CaptionHeight="0" CornerRadius="20"
                    GlassFrameThickness="0" ResizeBorderThickness="0"
                    UseAeroCaptionButtons="False"/>
    </Setter.Value>
  </Setter>
</Style>
```

Apply it: `<Window Style="{StaticResource IrspeedyWindowStyle}" ...>`.

`AllowsTransparency="False"` is deliberate — the approved design is **fully opaque** (an earlier translucent/Mica draft was explicitly reverted). Do not add blur effects or acrylic materials.

Rounded corners with `AllowsTransparency="False"` need the `WindowChrome.CornerRadius` above (works on Windows 11 without a transparent window, avoiding the perf/text-rendering cost of `AllowsTransparency="True"`).

### 1.2 Custom header (minimize/close, draggable)

Every screen except Settings shares this header pattern. Extract it as a `UserControl`, `AppHeader.xaml`:

```xml
<UserControl x:Class="Irspeedy.Controls.AppHeader" ...
             Height="{StaticResource HeaderHeight}">
  <Grid Background="Transparent" MouseLeftButtonDown="Header_DragMove">
    <StackPanel Orientation="Horizontal" Margin="12,0,18,0" VerticalAlignment="Center">
      <Image Source="/Assets/Logo/logo-header.png" Width="24" Height="32" Stretch="Uniform"/>
      <StackPanel FlowDirection="LeftToRight" Orientation="Horizontal"
                  Margin="10,0,0,0" VerticalAlignment="Bottom">
        <TextBlock Text="IRSPEEDY" FontSize="15" FontWeight="Bold"
                   Foreground="{StaticResource TextPrimaryBrush}"/>
        <TextBlock Text="v1.4.5.8" FontSize="11" Margin="8,0,0,0"
                   Foreground="{StaticResource TextSecondaryBrush}"
                   VerticalAlignment="Bottom"/>
      </StackPanel>
      <!-- Extra icon buttons (gear/shield) are injected per-screen via
           a ContentPresenter — see AppHeader.ExtraButtons dependency
           property, or just compose them inline per window instead of
           over-engineering a slot system. -->
    </StackPanel>
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Left"
                Margin="0,0,12,0" VerticalAlignment="Center">
      <Button Style="{StaticResource IconButtonStyle}" Click="Minimize_Click">
        <Path Data="M1.5,9.5 L10.5,9.5" Stroke="{StaticResource IconStrokeBrush}"
              StrokeThickness="1.4" StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
      </Button>
      <Button Style="{StaticResource IconButtonStyle}" Click="Close_Click">
        <Path Data="M1.5,1.5 L10.5,10.5 M10.5,1.5 L1.5,10.5"
              Stroke="{StaticResource IconStrokeBrush}" StrokeThickness="1.4"
              StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>
      </Button>
    </StackPanel>
  </Grid>
</UserControl>
```

Code-behind:
```csharp
private void Header_DragMove(object sender, MouseButtonEventArgs e) {
    if (e.LeftButton == MouseButtonState.Pressed)
        Window.GetWindow(this)?.DragMove();
}
private void Minimize_Click(object sender, RoutedEventArgs e) =>
    Window.GetWindow(this).WindowState = WindowState.Minimized;
private void Close_Click(object sender, RoutedEventArgs e) =>
    Window.GetWindow(this).Close();
```

`IconButtonStyle` (30×30, radius 8, transparent, hover tint) goes in `IrspeedyTheme.xaml`:
```xml
<Style x:Key="IconButtonStyle" TargetType="Button">
  <Setter Property="Width" Value="30"/>
  <Setter Property="Height" Value="30"/>
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="BorderThickness" Value="0"/>
  <Setter Property="Cursor" Value="Hand"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="Button">
        <Border x:Name="bg" CornerRadius="8" Background="{TemplateBinding Background}">
          <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="bg" Property="Background" Value="#0F111827"/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
```

### 1.3 Icons: convert SVG → WPF `Path`/`Geometry`

The handoff ships icons as SVG (viewBox `0 0 24 24` or similar). WPF doesn't render SVG natively. Two options:

- **Recommended**: convert each SVG path's `d="..."` attribute directly into a WPF `Path.Data` string (WPF's mini-language is a superset of SVG path syntax — most paths paste in unchanged). This is what the header/gear/swap examples above already do.
- **Alternative**: add the `SharpVectors` NuGet package (`SharpVectors.Converters`) and reference SVGs directly via `<svgc:SvgViewbox Source="/Assets/Icons/gear.svg"/>` — simpler for the ones with many paths (e.g. flags), more overhead for simple line icons.

For the **flags**, just use the pre-rendered PNGs in `Assets/Flags/` (76×76, already circular) inside a WPF `Ellipse` fill or plain `Image` — don't bother converting those to vector.

### 1.4 Persian-digit converter

Ping values, the connection timer, dates, and remaining-day counts must render in Persian (Arabic-Indic) digits; IP addresses and the version string stay Latin. Add a converter:

```csharp
public class PersianDigitConverter : IValueConverter {
    private static readonly char[] Persian = "۰۱۲۳۴۵۶۷۸۹".ToCharArray();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        var s = value?.ToString() ?? "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c >= '0' && c <= '9' ? Persian[c - '0'] : c);
        return sb.ToString();
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```
Register it as a resource (`<local:PersianDigitConverter x:Key="PersianDigits"/>`) and bind: `Text="{Binding PingMs, Converter={StaticResource PersianDigits}, StringFormat='{}{0} میلی‌ثانیه'}"`.

### 1.5 Toggle switch (Settings screen)

This is a **custom two-state control**, not a stock `CheckBox`/`ToggleButton` retemplate-only job — it needs the pill-with-colored-segment-and-thumb layout. Implement as a `UserControl`:

```xml
<!-- Controls/ToggleSwitch.xaml -->
<UserControl x:Class="Irspeedy.Controls.ToggleSwitch"
             Width="86" Height="32">
  <Grid Cursor="Hand" MouseLeftButtonUp="Root_Click">
    <Border CornerRadius="16" Background="{StaticResource ToggleTrackBrush}" Padding="3">
      <Grid x:Name="TrackGrid">
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="26"/>
        </Grid.ColumnDefinitions>
        <Border x:Name="ColorSegment" Grid.ColumnSpan="2" CornerRadius="13"
                Background="{StaticResource ToggleOnBrush}">
          <TextBlock x:Name="StateLabel" Text="ON" Foreground="White"
                     FontSize="11" FontWeight="Bold" HorizontalAlignment="Center"
                     VerticalAlignment="Center" Margin="0,0,14,0"/>
        </Border>
        <Border x:Name="Thumb" Grid.Column="1" Width="26" Height="26"
                CornerRadius="13" Background="White" HorizontalAlignment="Right">
          <Path x:Name="ThumbGlyph" Data="M7.2,1.8 3,5.5 7.2,9.2"
                Stroke="#5B6478" StrokeThickness="1.6"
                StrokeStartLineCap="Round" StrokeEndLineCap="Round"
                Width="11" Height="11" Stretch="Uniform"/>
        </Border>
      </Grid>
    </Border>
  </Grid>
</UserControl>
```

Code-behind — a bindable `IsOn` dependency property that flips the segment color, label text, thumb side, and chevron direction:

```csharp
public partial class ToggleSwitch : UserControl {
    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(ToggleSwitch),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnIsOnChanged));

    public bool IsOn {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public ToggleSwitch() { InitializeComponent(); Render(); }

    private static void OnIsOnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ToggleSwitch)d).Render();

    private void Render() {
        if (IsOn) {
            ColorSegment.Background = (Brush)FindResource("ToggleOnBrush");
            StateLabel.Text = "ON";
            Thumb.HorizontalAlignment = HorizontalAlignment.Right;
            StateLabel.Margin = new Thickness(0, 0, 14, 0);
            ThumbGlyph.Data = Geometry.Parse("M7.2,1.8 3,5.5 7.2,9.2"); // ‹
        } else {
            ColorSegment.Background = (Brush)FindResource("ToggleOffBrush");
            StateLabel.Text = "OFF";
            Thumb.HorizontalAlignment = HorizontalAlignment.Left;
            StateLabel.Margin = new Thickness(14, 0, 0, 0);
            ThumbGlyph.Data = Geometry.Parse("M3.8,1.8 8,5.5 3.8,9.2"); // ›
        }
    }

    private void Root_Click(object sender, MouseButtonEventArgs e) => IsOn = !IsOn;
}
```

Usage in Settings: `<controls:ToggleSwitch IsOn="{Binding IsVpnEnabled, Mode=TwoWay}"/>`.

---

## 2. Screen 1 — Server List (`MainWindow.xaml`)

Layout, top to bottom, inside a `Grid`/`StackPanel` with `Margin="18,4,18,18"` (see `MainContentPadding` in the theme):

1. `AppHeader` at the top, **plus two extra icon buttons** (gear → opens `SettingsWindow`, shield → security info) inserted before the divider. Easiest approach: don't over-abstract — copy the header XAML into `MainWindow.xaml` directly and add the two buttons, rather than forcing every screen through one rigid `AppHeader` control with a slot system.
2. Search `TextBox` styled to match — 44px height, radius 12, placeholder via a `TextBlock` overlay bound to `Text.Length == 0 ? Visible : Collapsed` (WPF `TextBox` has no native placeholder).
3. Smart Location — a `Button` (not static content — it's clickable, triggers "connect to fastest server").
4. `ItemsControl` (not `ListBox` — no selection chrome needed) bound to `ObservableCollection<ServerViewModel>`, `ItemsPanel` = vertical `StackPanel` inside a `ScrollViewer`.
   ```csharp
   public class ServerViewModel {
       public string FlagAsset { get; set; }   // e.g. "/Assets/Flags/germany.png"
       public string Label { get; set; }        // "فرانکفورت - آلمان"
       public int PingMs { get; set; }           // 38
       public bool IsSelected { get; set; }
   }
   ```
   Seed data — **10 servers**, in this order (see `DESIGN-SPEC.md` §2 for the full table): Germany, Netherlands, Turkey, France, UK, USA, Canada, UAE, Singapore, Japan. Row `DataTemplate`: flag `Image` (38×38, `Ellipse` clip or pre-circular PNG) → `TextBlock` label → ping `TextBlock` (bound through `PersianDigitConverter`, green) → empty radio circle (`Ellipse`, stroke only, becomes filled on selection — wire this to your actual VPN-connect state, not just UI chrome).
5. Connect `Button` pinned to the bottom (`VerticalAlignment="Bottom"` or `Grid.Row` last row with `Height="56"`) — **on click, this should navigate/transition to `ConnectedWindow`** (see §4 for the recommended navigation approach).

---

## 3. Screen 2 — Login (`LoginWindow.xaml`)

- Header: logo + "IRSPEEDY" (no version tag, no gear/shield) + minimize/close.
- Brand block: a 200×200 `Border` (`CornerRadius="52"`) with a soft gradient `Background`, containing an `Image` (100×135, `Stretch="Uniform"`) of the logo — **this is deliberately large**, matching the Connected screen's hero treatment (an earlier 96×96 draft was explicitly superseded).
- Form: `PasswordBox` for the password field — note WPF's `PasswordBox.Password` **cannot be data-bound directly** (security restriction); either use an attached-property workaround (`PasswordBoxAssistant` pattern) or read `PasswordBox.Password` imperatively in the submit handler. Don't silently swap to a plain `TextBox`.
- Eye-toggle icon: toggles between `PasswordBox` (masked) and a `TextBox` (revealed) occupying the same grid cell, swapping `Visibility` — WPF has no native "show password" mode.
- No forgot-password link — don't add one; it was explicitly removed from the design.
- "ورود" button: on click, validate → call your auth service → on success, close `LoginWindow` and open `MainWindow` (or `ConnectedWindow` directly if you auto-connect post-login — confirm with product).

---

## 4. Screen 3 — Connected Status (`ConnectedWindow.xaml`)

- Header: same minimal variant as Login (**no gear/shield here** — those were explicitly removed from this screen; they only live on the Server List header).
- Hero: three concentric layers, easiest built as a `Grid` with overlapping children (`Grid.Row/Column` all `0,0`, or just absolute `Canvas`/`Margin` centering):
  1. `Ellipse` 336×336, `RadialGradientBrush` (`rgba(23,163,102,0.16)` center → transparent edge) — the glow.
  2. `Ellipse` 336×336 minus 52 (26px inset each side) = 284×284, `Stroke` only, `rgba(23,163,102,0.25)`, `StrokeThickness="2"`.
  3. `Ellipse`/`Border` 248×248, white→`#F3FBF7` gradient fill, containing the 124×168 logo `Image` centered.
  4. Check badge: `Ellipse` 60×60, solid `#17A366`, `Border` 6px white ring (easiest as two nested ellipses), with a checkmark `Path`, anchored bottom-center of the 336×336 group (`VerticalAlignment="Bottom"`, small negative or positive `Margin` to match the 4px offset in the mock).
- Server card: same visual pattern as Smart Location on the Server List screen. The trailing icon button is a **swap/change-server action** (`swap-server.svg` — 4-arrow exchange glyph) — it replaced an earlier text link "تغییر"; wire its `Click` to re-open the server list or a picker, not to a no-op.
- Account/subscription card: 3 rows, `Border` dividers between them (1px, `#EEF0F4`). Bind:
  - مدت زمان اتصال → a live timer (`DispatcherTimer`, tick every second, format `HH:mm:ss`, run through `PersianDigitConverter`).
  - تاریخ انقضا → from the user's subscription record, Persian digits, `dir="ltr"` equivalent is `FlowDirection="LeftToRight"` on that one `TextBlock` since it's a date string you don't want mirrored.
  - اعتبار باقی‌مانده → remaining days, green, bold.
- IP row: keep this **Latin digits**, wrap in `FlowDirection="LeftToRight"` (nested, since the window root is RTL) — same treatment as the "IRSPEEDY" wordmark in the header.
- Disconnect button: 46px height, red gradient, pinned to the bottom with the **same 18px bottom clearance as the Connect button** on the Server List screen (`Margin="0,0,0,0"` on the button itself, with the parent `StackPanel`/`Grid` using `Margin="22,4,22,18"` — see `MainContentPadding` token; this was an explicit spacing fix in the approved design, don't reintroduce the earlier 12px value).

**Navigation between Server List → Connected**: don't create a brand-new `Window` instance and re-run the connect animation from scratch each time — either (a) use one `Window` with a `Frame`/`ContentControl` that swaps between a `ServerListView` and `ConnectedView` user control, or (b) if you keep them as separate `Window`s, hide the previous one (`Visibility="Hidden"`, not `Close()`) so window position/state persists and you're not re-instantiating the whole VPN connection state machine's UI on every toggle. Confirm which pattern the rest of the app already uses before picking one — don't introduce a second navigation paradigm into an existing codebase.

---

## 5. Screen 4 — Settings (`SettingsWindow.xaml`)

This is a **separate modal window**, not a view swapped into the main chrome:

```csharp
var settings = new SettingsWindow { Owner = this };
settings.ShowDialog();
```

- Header (64px): centered `TextBlock` "تنظیمات سرویس" + a circular red close button (`Ellipse`/`Border` 30×30, `close-circle.svg` glyph) anchored to the **leading edge** in RTL (which renders on the visual right — verify against the mock, don't assume). No logo, no minimize (it's a modal, not a top-level window).
- 6 rows, each `Height="68"`, `CornerRadius="16"`, white card, in a `StackPanel`/`Grid` with `VerticalAlignment` distribution — the approved layout uses `justify-content: space-between` in the web mock, i.e. in WPF terms: put the 6 rows in a `Grid` with 6 equal `RowDefinition Height="*"` (not `Auto`) inside the scrollable content area, so they spread evenly across the available height instead of clumping at the top with dead space before the confirm button.
- Each row: label `TextBlock` (trailing side) + `ToggleSwitch` (leading side, from §1.5).
- Bind each toggle to a real setting (VPN kill-switch, system proxy, proxifier, messaging-app proxy, VOD/AI routing, game mode) — **persist these**, don't leave them as UI-only state; confirm the settings storage mechanism already used elsewhere in the app (registry, local config file, etc.) and reuse it.
- Confirm button: full width, 50px, **neutral gray** (`#EDEFF3` fill, `#DCDFE6` border) — deliberately not accent-colored, this is intentional per the approved mock, don't "fix" it to blue.

Rows and defaults — see `DESIGN-SPEC.md` §5 for the exact 6 labels and ON/OFF defaults; don't invent different wording, this is the approved Persian copy.

---

## 6. QA checklist before you call this done

- [ ] All 4 windows render at exactly 420×700, 20px corner radius, fully opaque (no visible blur/transparency artifacts, no title bar).
- [ ] RTL flow is correct everywhere **except** the wordmark, IP address, and the connection-duration digits' container — those three are explicitly LTR-wrapped and must not mirror.
- [ ] Ping, duration, date, and remaining-day values render in Persian digits; IP and version stay Latin.
- [ ] Server List shows exactly the 10 countries in the documented order, each with the correct flag.
- [ ] Connected screen has **no** gear/shield icons in its header (regression risk — they were removed late in the design process).
- [ ] Connect button (Server List) and Disconnect button (Connected) sit the same 18px distance from the window's bottom edge.
- [ ] Login and Connected logos are the enlarged sizes (100×135 in a 200×200 badge; 124×168 in a 248×248 circle) — not the original small 56×75 size from an early draft.
- [ ] Settings toggle rows fill the available vertical space evenly (no dead gap above the "تایید" button).
- [ ] `PasswordBox` masking + eye-toggle actually works (common WPF gotcha — verify it isn't bound in a way that silently breaks the security restriction).
- [ ] Vazirmatn is bundled and loads from `pack://application:,,,/Fonts/#Vazirmatn` — not from a network Google Fonts URL (that was only ever for the browser mockup).
- [ ] Window dragging works via the custom header (no native titlebar to fall back on).

---

## 7. Delivering the change

Per the existing workflow for this repo: push isn't available directly, so package the change as a patch series and hand it over for `git am`:

```bash
git format-patch origin/main --stdout > irspeedy-4-screens.patch
```
or, if working in small reviewable commits, a numbered series (`0001-...`, `0002-...`) via `git format-patch -N`. Keep the XAML/asset commits separate from any VPN-engine/business-logic commits so the design work is reviewable on its own.
