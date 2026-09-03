using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Up2Ai.Services;

/// <summary>
/// رسمِ پرسشِ کپچا به‌صورت SVG درون‌خطی.
///
/// چرا SVG و نه تصویر: هیچ کتابخانه‌ی گرافیکی لازم ندارد (پروژه بدون
/// وابستگی است)، حجمش چند صد بایت است، درخواستِ شبکه‌ی جداگانه نمی‌خواهد
/// چون داخل خودِ HTML می‌نشیند، و روی هر تراکمِ پیکسلی تیز می‌ماند.
///
/// اعوجاج عمدی است تا متن مثل یک رشته‌ی ساده خوانده نشود: هر رقم چرخش و
/// جابه‌جاییِ کمی دارد و دو خطِ نویز از روی کاراکترها رد می‌شود. مقدارها
/// محدود نگه داشته شده‌اند تا برای چشمِ انسان همچنان راحت بماند — کپچایی
/// که کاربر نتواند بخواند، فرم را از خودِ ربات بهتر می‌بندد.
/// </summary>
public static class CaptchaSvg
{
    public static string Render(string question)
    {
        const int w = 132, h = 44;
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {w} {h}\" width=\"{w}\" height=\"{h}\" role=\"img\">");

        // نامِ دسترس‌پذیر تصویر. عمداً *خودِ عبارت* را نمی‌گوید — وگرنه کپچا
        // بی‌معنا می‌شد — فقط توضیح می‌دهد این تصویر چیست. بدون این، ابزارهای
        // بررسی دسترس‌پذیری (و صفحه‌خوان‌ها) یک تصویرِ بی‌نام می‌دیدند.
        sb.Append("<title>تصویر پرسش امنیتی</title>");
        sb.Append("<rect width=\"100%\" height=\"100%\" rx=\"10\" fill=\"#f1f1f8\"/>");

        // کاراکترها از راست به چپ چیده نمی‌شوند: خودِ عبارت «۳ + ۵» یک عبارت
        // ریاضی است و ترتیبش در هر جهتی یکی است، پس ساده از چپ می‌چینیم.
        var chars = question.Replace(" ", "");
        var step = (float)w / (chars.Length + 1);
        for (var i = 0; i < chars.Length; i++)
        {
            var x = step * (i + 1);
            var y = h / 2f + Rand(-3, 3);
            var rot = Rand(-14, 14);
            var size = 21 + Rand(-2, 2);
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{x:0.#}\" y=\"{y:0.#}\" fill=\"#312e81\" font-size=\"{size}\" font-weight=\"700\" " +
                $"font-family=\"Vazirmatn,Tahoma,sans-serif\" text-anchor=\"middle\" dominant-baseline=\"central\" " +
                $"transform=\"rotate({rot:0.#} {x:0.#} {y:0.#})\">{Esc(chars[i])}</text>");
        }

        // دو خطِ نویزِ کم‌رنگ — به‌اندازه‌ای که تشخیصِ خودکار سخت‌تر شود ولی
        // چشم همچنان رقم‌ها را جدا کند.
        for (var i = 0; i < 2; i++)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<path d=\"M0 {Rand(8, h - 8)} Q {w / 2} {Rand(2, h - 2)} {w} {Rand(8, h - 8)}\" fill=\"none\" stroke=\"#6366f1\" stroke-opacity=\".35\" stroke-width=\"1.4\"/>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    private static int Rand(int min, int maxInclusive) => RandomNumberGenerator.GetInt32(min, maxInclusive + 1);

    private static string Esc(char c) => c switch
    {
        '<' => "&lt;",
        '>' => "&gt;",
        '&' => "&amp;",
        _ => c.ToString(),
    };
}
