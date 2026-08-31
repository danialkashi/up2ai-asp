using System.Collections;
using System.Text.Json.Nodes;

namespace Up2Ai.Services;

/// <summary>
/// یک پوشش نازک دور <see cref="JsonNode"/> تا قالب‌های Razor خوانا بمانند.
///
///     @c["copy"]["hero"]["title"]        → متن
///     @foreach (var s in c["services"]["readyAgents"]) { … }
///     @if (c["contact"]["whatsapp"].HasText) { … }
///
/// چرا JsonNode و نه کلاس‌های تایپ‌دار: محتوای این سایت یک درخت JSON است که
/// «شکل»ش را فایل `defaults.json` تعیین می‌کند. با همین مدل، پنل مدیریت
/// می‌تواند فرم ویرایش را *خودکار* از روی شکل پیش‌فرض بسازد — دقیقاً همان
/// کاری که نسخه‌ی قبلی می‌کرد. اگر ده‌ها کلاس C# تعریف می‌کردیم، هر فیلد تازه
/// باید دو جا اضافه می‌شد و پنل و محتوا از هم می‌افتادند.
///
/// دسترسی به کلید یا اندیسِ نبوده خطا نمی‌دهد؛ یک مقدار «خالی» برمی‌گرداند تا
/// یک تایپوی ساده کل صفحه را پایین نیاورد.
/// </summary>
public readonly struct Cv : IEnumerable<Cv>
{
    private readonly JsonNode? _node;

    public Cv(JsonNode? node) => _node = node;

    public JsonNode? Node => _node;

    public Cv this[string key] =>
        new(_node is JsonObject o && o.TryGetPropertyValue(key, out var v) ? v : null);

    public Cv this[int index] =>
        new(_node is JsonArray a && index >= 0 && index < a.Count ? a[index] : null);

    /// <summary>متن این مقدار (اگر متن نبود، رشته‌ی خالی).</summary>
    public string S => _node is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";

    public bool B => _node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public int I => _node is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;

    public bool HasText => S.Length > 0;

    public bool Exists => _node is not null;

    public int Count => _node is JsonArray a ? a.Count : 0;

    public IEnumerator<Cv> GetEnumerator()
    {
        if (_node is not JsonArray a) yield break;
        foreach (var item in a) yield return new Cv(item);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>تا در Razor بشود مستقیم `@c["x"]["y"]` نوشت.</summary>
    public override string ToString() => S;
}
