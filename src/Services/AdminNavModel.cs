namespace Up2Ai.Services;

/// <summary>
/// ورودیِ پارشالِ ناوبری پنل. یک بار در سایدبار دسکتاپ و یک بار در منوی
/// تاشوی موبایل رندر می‌شود، پس در یک جا نگه داشته می‌شود تا آن دو هیچ‌وقت
/// از هم جدا نیفتند.
/// </summary>
/// <param name="Available">بخش‌هایی که واقعاً در محتوا وجود دارند.</param>
/// <param name="ActiveTab">کلید بخشِ باز.</param>
/// <param name="OnHome">روی خانه‌ی پنل هستیم؟</param>
/// <param name="OnLeads">روی صندوق لید هستیم؟</param>
public sealed record AdminNavModel(
    List<string> Available,
    string ActiveTab,
    bool OnHome,
    bool OnLeads);
