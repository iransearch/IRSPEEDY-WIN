فونت Vazirmatn — چرا کیفیت پایین می‌آید و چطور درست embed شود (WPF)
======================================================================

علت اصلی
---------
در طراحی (مرجع وب/HTML)، فونت با این خط بارگذاری می‌شود:
  @import url('https://fonts.googleapis.com/css2?family=Vazirmatn:...')

این یک "وب‌فونت" است که مرورگر در لحظه از اینترنت دانلود می‌کند. Vazirmatn روی
ویندوز به‌صورت پیش‌فرض نصب نیست. اگر در کد WPF فقط بنویسید:
  FontFamily="Vazirmatn"
و خودِ فایل فونت را داخل پروژه قرار نداده باشید، ویندوز این نام را پیدا نمی‌کند
و بی‌صدا (بدون خطا) به یک فونت جایگزین سیستم (معمولاً Segoe UI یا Tahoma) سقوط
می‌کند — دقیقاً همین چیزی است که در اسکرین‌شات‌های شما دیده می‌شود: حروف نازک‌تر،
اعداد فارسی با فرم متفاوت، و کلی ظاهر کم‌کیفیت‌تر نسبت به طراحی.

مشکل دوم: Vazirmatn یک Variable Font است (یک فایل با طیف پیوسته وزن از نازک
تا ضخیم). WPF (به‌خصوص .NET Framework) از محور وزن (weight axis) فونت‌های
Variable به‌درستی پشتیبانی نمی‌کند؛ یعنی حتی اگر همان یک فایل Variable را
embed کنید، تنظیم FontWeight="Bold" ممکن است اثر نکند و همه‌چیز با وزن
پیش‌فرض (نازک) رندر شود. راه‌حل: به‌جای فایل Variable، از فایل‌های جداگانه
"Static" هر وزن استفاده کنید (همین فایل‌های پیوست‌شده).

فایل‌های پیوست — همان وزن‌هایی که در طراحی استفاده شده‌اند
------------------------------------------------------------
Vazirmatn-Regular.ttf     → وزن 400 (متن‌های عادی، زیرنویس‌ها)
Vazirmatn-Medium.ttf      → وزن 500
Vazirmatn-SemiBold.ttf    → وزن 600
Vazirmatn-Bold.ttf        → وزن 700 (اکثر لیبل‌ها)
Vazirmatn-ExtraBold.ttf   → وزن 800 (تیترها، مثل «تنظیمات سرویس»)

نحوه Embed کردن در پروژه WPF
------------------------------
۱) یک پوشه به نام Fonts در ریشه پروژه بسازید و این ۵ فایل ttf را داخلش کپی کنید.
۲) در Visual Studio، روی هر فایل فونت کلیک راست → Properties:
      Build Action:      Resource
      Copy to Output:    Do not copy (چون به‌صورت Resource داخل exe/dll کامپایل می‌شود)
۳) در XAML، برای هر وزن باید FontFamily را دقیقاً این‌طور بنویسید (نه صرفاً
   "Vazirmatn")، چون هر فایل داخلی خودش یک نام جدا دارد. نام دقیق داخل هر فایل
   (استخراج‌شده و تأییدشده) این است — همین‌ها را عیناً استفاده کنید:

     Vazirmatn-Regular.ttf    → نام داخلی: Vazirmatn   (وزن پیش‌فرض 400)
     Vazirmatn-Medium.ttf     → نام داخلی: Vazirmatn Medium
     Vazirmatn-SemiBold.ttf   → نام داخلی: Vazirmatn SemiBold
     Vazirmatn-Bold.ttf       → نام داخلی: Vazirmatn   (همراه با FontWeight="Bold")
     Vazirmatn-ExtraBold.ttf  → نام داخلی: Vazirmatn ExtraBold

   مثال:
   <TextBlock Text="تنظیمات سرویس"
              FontFamily="pack://application:,,,/Fonts/#Vazirmatn ExtraBold"
              FontSize="16"/>

   <TextBlock Text="شخصی‌سازی رفتار اتصال"
              FontFamily="pack://application:,,,/Fonts/#Vazirmatn Medium"
              FontSize="10.5"/>

   <TextBlock Text="تایید و ذخیره"
              FontFamily="pack://application:,,,/Fonts/#Vazirmatn"
              FontWeight="Bold"
              FontSize="14.5"/>

   نکته مهم: چون Regular.ttf و Bold.ttf هر دو زیر همان نام خانواده "Vazirmatn"
   ثبت شده‌اند (بر خلاف Medium/SemiBold/ExtraBold که هرکدام نام خانواده مستقل
   دارند)، اگر هر دو فایل هم‌زمان embed شوند، WPF می‌تواند FontWeight="Bold"
   را روی همان FontFamily="...#Vazirmatn" به‌درستی به فایل Bold.ttf نگاشت کند.
   برای Medium/SemiBold/ExtraBold این نگاشت خودکار وجود ندارد، پس باید مستقیم
   نام کامل هرکدام (طبق جدول بالا) را در FontFamily بنویسید، نه FontWeight.

۴) اگر پروژه چندین صفحه/فایل XAML دارد، بهتر است این‌ها را در App.xaml به‌صورت
   Style یا StaticResource مرکزی تعریف کنید تا در همه صفحات یکسان اعمال شود
   و اشتباهاً یک صفحه از فونت سیستم استفاده نکند.

نکته تکمیلی درباره کیفیت رندر متن
-----------------------------------
بعد از embed درست فونت، اگر باز هم متن محو/تار به نظر رسید (مخصوصاً روی
مانیتورهایی با مقیاس ۱۲۵٪/۱۵۰٪)، این خط را هم در ریشه Window اضافه کنید — این
مستقل از مشکل فونت است و به مشکل DPI که قبلاً توضیح داده شد مرتبط می‌شود:

   TextOptions.TextFormattingMode="Display"
   TextOptions.TextRenderingMode="ClearType"

و مطمئن شوید app.manifest با PerMonitorV2 DPI-aware است (طبق توضیح قبلی)،
چون بدون آن، کل پنجره از جمله متن‌ها با bitmap scaling کِش داده می‌شود و حتی
با فونت درست هم محو دیده می‌شود.

مجوز فونت
----------
Vazirmatn تحت SIL Open Font License 1.1 منتشر شده (فایل OFL.txt پیوست است)؛
استفاده و توزیع آن در اپلیکیشن تجاری آزاد است، فقط فایل مجوز باید همراه
پروژه نگه داشته شود.
