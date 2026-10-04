# بررسی کرش اشتراک‌گذاری VPN در لاگ ۲۰۲۶-۱۰-۰۴

## نتیجه

کرش ثبت‌شده از انیمیشن نمای اشتراک‌گذاری پراکسی آغاز می‌شود. خطای ثبت‌شده:

```text
System.InvalidOperationException:
Cannot animate '(0).(1)' on an immutable object instance.
```

مسیر خطا از `ShareVPNSetting.RefreshProxyUi` به `ProxySharingMotion.UpdateMotion`
و سپس `Storyboard.ProcessComplexPath` می‌رسد. هنگام نمایش نمای فعال پراکسی،
شروع انیمیشن شکست می‌خورد و خطا به dispatcher برنامه منتقل می‌شود.

## شواهد لاگ

چهار زمان مستقل برای این خطا ثبت شده است؛ هر خطا توسط دو handler ثبت شده و
در مجموع هشت ورودی `[Crashed]` دارد. زمان‌ها همان زمان درج‌شده در لاگ هستند:

| زمان | خطا | نقطهٔ شروع در برنامه |
| --- | --- | --- |
| 16:52:52 | immutable animation target | `ProxySharingMotion.UpdateMotion` |
| 16:56:00 | immutable animation target | `ProxySharingMotion.UpdateMotion` |
| 20:27:09 | immutable animation target | `ProxySharingMotion.UpdateMotion` |
| 20:37:40 | immutable animation target | `ProxySharingMotion.UpdateMotion` |

نسخهٔ گزارش‌شده در تلاش‌های اشتراک مستقیم `1.4.6.4` است.

## اصلاح در نسخهٔ 1.4.6.5

انیمیشن، `Tunnel.Stroke.Opacity` را تغییر می‌دهد. پیش از اصلاح، `Stroke`
مستقیماً به قلم مشترک پالت `Theme.AccentLineBrush` اشاره می‌کرد. قلم‌های
منابع WPF ممکن است frozen باشند و شروع انیمیشن روی این مسیر پیچیده به
جایگزینی یک clone نیاز داشته باشد.

اکنون `Tunnel` یک `SolidColorBrush` مستقل دارد. رنگ این قلم با
`DynamicResource Theme.AccentLineColor` از تم جاری خوانده می‌شود؛ این عبارت
پویا قلم را قابل تغییر نگه می‌دارد. انیمیشن روی قلم اختصاصی همان نما اجرا
می‌شود و منابع مشترک پالت و نماهای دیگر را تغییر نمی‌دهد. رنگ و زمان‌بندی
انیمیشن حفظ می‌شوند.

## دو خطای جداگانهٔ هات‌اسپات

در 17:01:49 و 17:02:45 خطای `helper-response-timeout` ثبت شده است. در هر دو
تلاش، helper با backend `wifi-direct` و ویندوز `10.0.26100.0` آماده شده،
اما پاسخ شروع در مهلت ۳۰ ثانیه دریافت نشده است. آماده‌سازی بسته در تلاش
اول حدود ۲۳ ثانیه طول کشیده و در تلاش دوم از بستهٔ آماده استفاده شده است؛
مهلت دریافت پاسخ بعد از آماده‌شدن helper شروع می‌شود.

این خطاها مسیر stack کرش انیمیشن را ندارند و شواهد موجود مرحلهٔ دقیق توقف
در helper را نشان نمی‌دهد. اصلاح قلم گرافیکی، رفع این زمان‌انتظار را
اثبات نمی‌کند؛ برای تشخیص آن باید شواهد مرحلهٔ شروع، publisher و ICS از
helper در دسترس باشد.

## بررسی‌ها

- بررسی XAML، منابع تم و خوانایی رنگ‌ها در این محیط اجرا شد و موفق بود.
- آزمون پراکسی، snapshotهای قدیمی و تغییر جلسهٔ VPN را بررسی کرد و موفق بود.
- آزمون ویندوزی `tests/SharingMotionChecks/run-wpf.ps1`، کنترل واقعی برنامه
  را با قلم‌های frozen پالت اجرا می‌کند؛ دو نما، تغییر تم، نمایش/پنهان‌شدن،
  unload و ساخت مجدد نما را می‌آزماید. VPN یا هات‌اسپات راه‌اندازی نمی‌کند.
- اجرای واقعی WPF در محیط لینوکسی این بررسی ممکن نیست. آزمون ویندوزی را
  باید روی بیلد unobfuscated اجرا کرد:

```powershell
powershell.exe -NoProfile -STA -File tests/SharingMotionChecks/run-wpf.ps1 -AppPath <IRSpeedyVPN.exe>
```
