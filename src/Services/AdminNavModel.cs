namespace Up2Ai.Services;

/// <summary>
/// ورودیِ نوارِ تب‌های پنل (<c>_AdminNav.cshtml</c>).
///
/// نوار در یک ردیفِ افقی زیر هدر رندر می‌شود — همان ناوبری در همه‌ی عرض‌ها.
/// (ستون کناری امتحان شد و کنار گذاشته شد؛ توضیحش در <c>_AdminLayout</c>.)
///
/// تب‌ها دو دسته‌اند: بخش‌های *محتوا* که از خودِ شکلِ داده می‌آیند
/// (<paramref name="Available"/>)، و صفحه‌های ثابتِ پنل مثل صندوق لید و
/// وبلاگ که کلیدِ محتوایی ندارند و با <paramref name="ActiveExtra"/> مشخص
/// می‌شوند.
/// </summary>
/// <param name="Available">بخش‌هایی که واقعاً در محتوا وجود دارند.</param>
/// <param name="ActiveTab">کلید بخشِ محتوایی که باز است.</param>
/// <param name="OnHome">روی خانه‌ی پنل هستیم؟</param>
/// <param name="ActiveExtra">کدام صفحه‌ی ثابت باز است: leads / posts / comments / users.</param>
/// <param name="PendingComments">
/// نظرهای منتظرِ تأیید — روی تبِ نظرها به‌صورت نشانِ عددی دیده می‌شود.
/// بدون این نشان، تنها راهِ فهمیدنِ نظرِ تازه سر زدنِ دستی به آن صفحه بود.
/// </param>
public sealed record AdminNavModel(
    List<string> Available,
    string ActiveTab,
    bool OnHome,
    string ActiveExtra,
    int PendingComments);
