using System.Text.Json.Nodes;

namespace Up2Ai.Services;

/// <summary>
/// گزارش این‌که چه چیزی پذیرفته و چه چیزی رد شد (برای پیام دادن در پنل).
/// </summary>
public sealed class MergeReport
{
    public List<string> Applied { get; } = new();
    public List<string> Rejected { get; } = new();
}

/// <summary>
/// ادغام شکل‌محورِ ویرایش‌ها روی پیش‌فرض‌ها.
///
/// قانون: پیش‌فرض تعیین می‌کند چه چیزی *مجاز* است. هر مقداری که از فایل
/// ویرایش‌ها می‌آید فقط وقتی پذیرفته می‌شود که نوعش با همان جای پیش‌فرض یکی
/// باشد. کلید ناشناخته، نوع اشتباه، یا null بی‌سروصدا نادیده گرفته می‌شود و
/// پیش‌فرض سر جایش می‌ماند.
///
/// چرا این‌قدر سخت‌گیر: منبع این داده یک فایل روی دیسک است که آدم هم می‌تواند
/// دستی ویرایشش کند. یک کاماً جا افتاده نباید کل سایت را پایین بیاورد.
///
/// تابع خالص است — نه فایل می‌خواند نه چیزی می‌نویسد — تا بشود مستقیم تستش کرد.
/// (این پیاده‌سازی معادل دقیق `src/lib/content/merge.ts` در نسخه‌ی Next.js است.)
/// </summary>
public static class ShapeMerge
{
    public static (JsonNode Content, MergeReport Report) Merge(JsonNode defaults, JsonNode? overrides)
    {
        var report = new MergeReport();
        if (overrides is not JsonObject)
        {
            return (defaults.DeepClone(), report);
        }
        var merged = MergeValue(defaults, overrides, "", report) ?? defaults.DeepClone();
        return (merged, report);
    }

    private static JsonNode? MergeValue(JsonNode? def, JsonNode? next, string path, MergeReport report)
    {
        // آرایه: طول می‌تواند فرق کند (افزودن/حذف آیتم)، ولی شکل هر آیتم باید با
        // شکل *اولین* آیتم پیش‌فرض بخواند. آرایه‌ی پیش‌فرضِ خالی هر آرایه‌ای را
        // می‌پذیرد، چون الگویی برای مقایسه ندارد.
        if (def is JsonArray defArray)
        {
            if (next is not JsonArray nextArray)
            {
                report.Rejected.Add(path);
                return def.DeepClone();
            }

            var template = defArray.Count > 0 ? defArray[0] : null;
            if (template is null)
            {
                report.Applied.Add(path);
                return nextArray.DeepClone();
            }

            var outArray = new JsonArray();
            for (var i = 0; i < nextArray.Count; i++)
            {
                outArray.Add(MergeValue(template, nextArray[i], $"{path}[{i}]", report));
            }
            report.Applied.Add(path);
            return outArray;
        }

        if (def is JsonObject defObject)
        {
            if (next is not JsonObject nextObject)
            {
                report.Rejected.Add(path);
                return def.DeepClone();
            }

            var outObject = (JsonObject)defObject.DeepClone();
            foreach (var pair in nextObject)
            {
                if (!defObject.ContainsKey(pair.Key))
                {
                    // کلیدی که در پیش‌فرض نیست یعنی یا اشتباه تایپی است یا از
                    // نسخه‌ی قدیمی مانده. نگهش نمی‌داریم تا شکل داده هرگز از
                    // مدل جدا نیفتد.
                    //
                    // در ریشه، path خالی است؛ بدون این شرط نامِ کلید با یک
                    // نقطه‌ی اضافه شروع می‌شد («‎.thisKey») و در هشدارِ پنل
                    // همان شکلِ عجیب به کاربر نشان داده می‌شد.
                    report.Rejected.Add(string.IsNullOrEmpty(path) ? pair.Key : $"{path}.{pair.Key}");
                    continue;
                }

                var childPath = string.IsNullOrEmpty(path) ? pair.Key : $"{path}.{pair.Key}";
                outObject[pair.Key] = MergeValue(defObject[pair.Key], pair.Value, childPath, report);
            }
            return outObject;
        }

        // مقدار ساده: فقط اگر هم‌نوع بود
        if (next is JsonValue nextValue && def is JsonValue defValue && SameKind(defValue, nextValue))
        {
            report.Applied.Add(path);
            return nextValue.DeepClone();
        }

        report.Rejected.Add(path);
        return def?.DeepClone();
    }

    /// <summary>
    /// معادل `typeof next === typeof def` در جاوااسکریپت: فقط سه نوع ساده‌ای که
    /// در محتوای این سایت وجود دارند (رشته، عدد، بولین) با هم مقایسه می‌شوند.
    /// </summary>
    private static bool SameKind(JsonValue a, JsonValue b)
    {
        if (a.TryGetValue<string>(out _)) return b.TryGetValue<string>(out _);
        if (a.TryGetValue<bool>(out _)) return b.TryGetValue<bool>(out _);
        if (a.TryGetValue<double>(out _)) return b.TryGetValue<double>(out _) && !b.TryGetValue<string>(out _);
        return false;
    }
}
