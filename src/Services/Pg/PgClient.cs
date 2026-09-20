using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Up2Ai.Services.Pg;

/// <summary>
/// یک کلاینتِ کوچکِ PostgreSQL که مستقیم با پروتکل سیمیِ خودِ پستگرس حرف می‌زند.
///
/// ─────────────────────────────────────────────────────────────────────────
/// چرا دستی نوشته شده و از Npgsql استفاده نشده؟
///
/// این پروژه از روز اول یک قاعده داشته: هیچ بسته‌ی بیرونی. کپچا، مارک‌داون،
/// تقویم فارسی و ذخیره‌سازی فایلی همه به همین دلیل دست‌نویس‌اند (NuGet.config
/// را ببین). دلیلش هم ساده است: سایت باید روی هر هاستی، بدون دسترسی به
/// اینترنتِ زمانِ بیلد و بدون زنجیره‌ی وابستگی، ساخته و مستقر شود.
///
/// این کلاس همان قاعده را نگه می‌دارد، ولی حدّ خودش را هم می‌داند:
///
///   • فقط چیزی را پیاده کرده که این سایت لازم دارد — پرس‌وجوی پارامتری،
///     تراکنش، و همین. نه COPY، نه LISTEN/NOTIFY، نه نوعِ باینری.
///   • همه‌ی مقدارها در قالب *متن* رد و بدل می‌شوند. برای داده‌ای که خودش
///     JSON و رشته است، این ساده‌ترین و کم‌اشتباه‌ترین راه است.
///   • احراز هویت: trust، cleartext، MD5 و SCRAM-SHA-256 (پیش‌فرضِ پستگرس ۱۴+).
///   • TLS پشتیبانی می‌شود (sslmode=require) — برای دیتابیس ابری لازم است.
///
/// اگر روزی حجم یا نیازها از این فراتر رفت، جای درستِ عوض کردن همین یک فایل
/// است: بقیه‌ی برنامه فقط <see cref="QueryAsync"/> و <see cref="ExecuteAsync"/>
/// را می‌شناسد، نه پروتکل را.
/// ─────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class PgClient : IAsyncDisposable
{
    private readonly PgConnectionInfo _info;
    private readonly ConcurrentBag<PgSession> _idle = new();
    private readonly SemaphoreSlim _slots;
    private readonly ILogger _log;
    private bool _disposed;

    public PgClient(PgConnectionInfo info, ILogger log, int maxConnections = 16)
    {
        _info = info;
        _log = log;
        _slots = new SemaphoreSlim(maxConnections, maxConnections);
    }

    public PgConnectionInfo Info => _info;

    /// <summary>یک اتصال از استخر می‌گیرد؛ Dispose آن را برمی‌گرداند.</summary>
    public async Task<PgLease> LeaseAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _slots.WaitAsync(ct);
        try
        {
            while (_idle.TryTake(out var pooled))
            {
                // اتصالی که سرور بسته باشد نباید به فراخوان داده شود.
                if (pooled.IsUsable) return new PgLease(this, pooled);
                await pooled.CloseAsync();
            }

            var fresh = await PgSession.OpenAsync(_info, ct);
            return new PgLease(this, fresh);
        }
        catch
        {
            _slots.Release();
            throw;
        }
    }

    internal void Return(PgSession session, bool broken)
    {
        if (broken || _disposed || !session.IsUsable)
        {
            _ = session.CloseAsync();
        }
        else
        {
            _idle.Add(session);
        }
        _slots.Release();
    }

    /// <summary>پرس‌وجویی که سطر برمی‌گرداند.</summary>
    public async Task<List<PgRow>> QueryAsync(string sql, params object?[] parameters)
    {
        using var lease = await LeaseAsync();
        return await lease.Session.QueryAsync(sql, parameters);
    }

    /// <summary>دستوری که سطر برنمی‌گرداند؛ تعداد سطرهای متأثر را می‌دهد.</summary>
    public async Task<int> ExecuteAsync(string sql, params object?[] parameters)
    {
        using var lease = await LeaseAsync();
        return await lease.Session.ExecuteAsync(sql, parameters);
    }

    /// <summary>چند دستور پشت سر هم (برای DDL). پارامتر نمی‌گیرد.</summary>
    public async Task ExecuteScriptAsync(string sql)
    {
        using var lease = await LeaseAsync();
        await lease.Session.SimpleQueryAsync(sql);
    }

    /// <summary>یک بار وصل می‌شود تا معلوم شود تنظیمات درست است.</summary>
    public async Task<string> PingAsync()
    {
        var rows = await QueryAsync("select version()");
        return rows.Count > 0 ? rows[0].GetString(0) ?? "" : "";
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        while (_idle.TryTake(out var s)) await s.CloseAsync();
    }
}

/// <summary>اتصالِ قرض‌گرفته‌شده از استخر. Dispose آن را برمی‌گرداند.</summary>
public sealed class PgLease : IDisposable
{
    private readonly PgClient _owner;
    private bool _returned;

    internal PgLease(PgClient owner, PgSession session)
    {
        _owner = owner;
        Session = session;
    }

    public PgSession Session { get; }

    /// <summary>اگر وسطِ کار خطای پروتکلی رخ داد، اتصال دیگر قابل اعتماد نیست.</summary>
    public void MarkBroken() => Session.Broken = true;

    public void Dispose()
    {
        if (_returned) return;
        _returned = true;
        _owner.Return(Session, Session.Broken);
    }
}

/// <summary>اطلاعات اتصال، از رشته‌ی اتصال یا از URL.</summary>
public sealed record PgConnectionInfo(
    string Host, int Port, string Database, string Username, string Password, PgSslMode SslMode)
{
    /// <summary>
    /// هر دو نوشتار را می‌پذیرد:
    ///   postgres://user:pass@host:5432/db?sslmode=require
    ///   Host=...;Port=5432;Database=...;Username=...;Password=...;SslMode=Require
    /// </summary>
    public static PgConnectionInfo Parse(string raw)
    {
        var text = (raw ?? "").Trim();
        if (text.Length == 0) throw new ArgumentException("رشته‌ی اتصال خالی است.", nameof(raw));

        if (text.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(text);
            var userInfo = Uri.UnescapeDataString(uri.UserInfo);
            var sep = userInfo.IndexOf(':');
            var user = sep >= 0 ? userInfo[..sep] : userInfo;
            var pass = sep >= 0 ? userInfo[(sep + 1)..] : "";
            var db = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            var ssl = PgSslMode.Prefer;
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=', 2);
                if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                    ssl = ParseSsl(kv[1]);
            }
            return new PgConnectionInfo(uri.Host, uri.Port > 0 ? uri.Port : 5432, db, user, pass, ssl);
        }

        string host = "127.0.0.1", database = "", username = "", password = "";
        var port = 5432;
        var sslMode = PgSslMode.Prefer;
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToLowerInvariant();
            var value = kv[1].Trim();
            switch (key)
            {
                case "host" or "server": host = value; break;
                case "port": if (int.TryParse(value, out var p)) port = p; break;
                case "database" or "db": database = value; break;
                case "username" or "user id" or "user": username = value; break;
                case "password" or "pwd": password = value; break;
                case "sslmode" or "ssl mode": sslMode = ParseSsl(value); break;
            }
        }
        if (database.Length == 0 || username.Length == 0)
            throw new ArgumentException("رشته‌ی اتصال باید دست‌کم Database و Username داشته باشد.", nameof(raw));
        return new PgConnectionInfo(host, port, database, username, password, sslMode);
    }

    private static PgSslMode ParseSsl(string v) => v.Trim().ToLowerInvariant() switch
    {
        "disable" or "false" or "off" => PgSslMode.Disable,
        "verify-ca" or "verify-full" or "verifyfull" => PgSslMode.VerifyFull,
        "require" or "true" or "on" => PgSslMode.Require,
        _ => PgSslMode.Prefer,
    };

    /// <summary>برای لاگ — رمز هرگز چاپ نمی‌شود.</summary>
    public string Safe => $"{Username}@{Host}:{Port}/{Database} (ssl={SslMode})";
}

public enum PgSslMode { Disable, Prefer, Require, VerifyFull }

/// <summary>یک سطر از نتیجه، با دسترسی به ستون‌ها بر اساس نام یا شماره.</summary>
public sealed class PgRow
{
    private readonly string?[] _values;
    private readonly Dictionary<string, int> _index;

    internal PgRow(string?[] values, Dictionary<string, int> index)
    {
        _values = values;
        _index = index;
    }

    public string? GetString(int ordinal) => _values[ordinal];

    public string? GetString(string column) =>
        _index.TryGetValue(column, out var i) ? _values[i] : null;

    public bool GetBool(string column) => GetString(column) is "t" or "true" or "1";

    public long GetLong(string column) =>
        long.TryParse(GetString(column), out var v) ? v : 0;
}

/// <summary>خطایی که خودِ پستگرس برگردانده است.</summary>
public sealed class PgException : Exception
{
    public PgException(string severity, string code, string message, string? detail)
        : base($"[{code}] {message}" + (detail is { Length: > 0 } ? $" — {detail}" : ""))
    {
        Severity = severity;
        SqlState = code;
    }

    public string Severity { get; }
    public string SqlState { get; }
}

/// <summary>یک اتصالِ زنده. یک‌نخی است — هم‌زمان از دو جا استفاده نشود.</summary>
public sealed class PgSession
{
    private const int ProtocolVersion3 = 196608;      // 3.0
    private const int SslRequestCode = 80877103;

    private TcpClient _tcp = null!;
    private Stream _stream = null!;
    private readonly byte[] _header = new byte[5];

    internal bool Broken { get; set; }

    public bool IsUsable => !Broken && _tcp is { Connected: true };

    public static async Task<PgSession> OpenAsync(PgConnectionInfo info, CancellationToken ct)
    {
        var s = new PgSession();
        await s.ConnectAsync(info, ct);
        return s;
    }

    private async Task ConnectAsync(PgConnectionInfo info, CancellationToken ct)
    {
        _tcp = new TcpClient { NoDelay = true };
        await _tcp.ConnectAsync(info.Host, info.Port, ct);
        _stream = _tcp.GetStream();

        if (info.SslMode != PgSslMode.Disable)
        {
            var buf = new byte[8];
            BinaryPrimitives.WriteInt32BigEndian(buf, 8);
            BinaryPrimitives.WriteInt32BigEndian(buf.AsSpan(4), SslRequestCode);
            await _stream.WriteAsync(buf, ct);
            await _stream.FlushAsync(ct);

            var reply = new byte[1];
            await ReadExactAsync(reply, 1, ct);
            if (reply[0] == (byte)'S')
            {
                // verify-full یعنی گواهی واقعاً بررسی شود (زنجیره + نام میزبان).
                // require مثل خودِ libpq فقط رمزگذاری می‌خواهد و گواهی را
                // بررسی نمی‌کند — چون بیشترِ دیتابیس‌های مدیریت‌شده گواهیِ
                // خودامضا دارند. هر کدام را بخواهی، صریح انتخاب می‌کنی.
                var verify = info.SslMode == PgSslMode.VerifyFull;
                var ssl = new SslStream(_stream, leaveInnerStreamOpen: false,
                    verify ? null : AcceptAnyCertificate);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = info.Host,
                }, ct);
                _stream = ssl;
            }
            else if (info.SslMode is PgSslMode.Require or PgSslMode.VerifyFull)
            {
                throw new InvalidOperationException("سرور TLS را نپذیرفت ولی sslmode=require خواسته شده بود.");
            }
        }

        await SendStartupAsync(info, ct);
        await AuthenticateAsync(info, ct);
        await WaitForReadyAsync(ct);
    }

    private static bool AcceptAnyCertificate(object _, X509Certificate? __, X509Chain? ___, SslPolicyErrors ____) => true;

    private async Task SendStartupAsync(PgConnectionInfo info, CancellationToken ct)
    {
        var body = new MemoryStream();
        WriteInt32(body, ProtocolVersion3);
        WriteCString(body, "user"); WriteCString(body, info.Username);
        WriteCString(body, "database"); WriteCString(body, info.Database);
        WriteCString(body, "client_encoding"); WriteCString(body, "UTF8");
        WriteCString(body, "application_name"); WriteCString(body, "up2ai");
        body.WriteByte(0);

        var payload = body.ToArray();
        var msg = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(msg, msg.Length);
        payload.CopyTo(msg, 4);
        await _stream.WriteAsync(msg, ct);
        await _stream.FlushAsync(ct);
    }

    /* ------------------------------ احراز هویت ------------------------------ */

    private async Task AuthenticateAsync(PgConnectionInfo info, CancellationToken ct)
    {
        while (true)
        {
            var (type, payload) = await ReadMessageAsync(ct);
            switch (type)
            {
                case (byte)'R':
                    var kind = BinaryPrimitives.ReadInt32BigEndian(payload);
                    switch (kind)
                    {
                        case 0: return;                                   // AuthenticationOk
                        case 3: await SendPasswordAsync(info.Password, ct); break;
                        case 5: await SendMd5Async(info, payload.AsSpan(4, 4).ToArray(), ct); break;
                        case 10: await ScramAsync(info, payload, ct); return;
                        default:
                            throw new NotSupportedException(
                                $"روش احراز هویت {kind} پشتیبانی نمی‌شود. " +
                                "روی سرور scram-sha-256 یا md5 را فعال کن.");
                    }
                    break;

                case (byte)'E': throw ReadError(payload);
                case (byte)'S' or (byte)'N': break;                        // ParameterStatus / Notice
                default: throw new InvalidOperationException($"پیام غیرمنتظره هنگام ورود: {(char)type}");
            }
        }
    }

    private async Task SendPasswordAsync(string password, CancellationToken ct)
    {
        var body = new MemoryStream();
        WriteCString(body, password);
        await SendAsync((byte)'p', body.ToArray(), ct);
    }

    private async Task SendMd5Async(PgConnectionInfo info, byte[] salt, CancellationToken ct)
    {
        // md5( md5(password + user) + salt ) — دقیقاً همان چیزی که پستگرس می‌خواهد.
        var inner = Md5Hex(Encoding.UTF8.GetBytes(info.Password + info.Username));
        var outer = Md5Hex(Encoding.UTF8.GetBytes(inner).Concat(salt).ToArray());
        await SendPasswordAsync("md5" + outer, ct);
    }

    private static string Md5Hex(byte[] data) =>
        Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

    /// <summary>SCRAM-SHA-256 بدون channel binding (RFC 7677).</summary>
    private async Task ScramAsync(PgConnectionInfo info, byte[] first, CancellationToken ct)
    {
        var mechanisms = ReadCStringList(first.AsSpan(4));
        if (!mechanisms.Contains("SCRAM-SHA-256"))
            throw new NotSupportedException("سرور SCRAM-SHA-256 را پیشنهاد نداد.");

        var clientNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var clientFirstBare = $"n=*,r={clientNonce}";
        var clientFirst = "n,," + clientFirstBare;

        var init = new MemoryStream();
        WriteCString(init, "SCRAM-SHA-256");
        var clientFirstBytes = Encoding.UTF8.GetBytes(clientFirst);
        WriteInt32(init, clientFirstBytes.Length);
        init.Write(clientFirstBytes);
        await SendAsync((byte)'p', init.ToArray(), ct);

        // AuthenticationSASLContinue
        var (t1, p1) = await ReadMessageAsync(ct);
        if (t1 == (byte)'E') throw ReadError(p1);
        if (t1 != (byte)'R' || BinaryPrimitives.ReadInt32BigEndian(p1) != 11)
            throw new InvalidOperationException("پاسخ SASLContinue نیامد.");
        var serverFirst = Encoding.UTF8.GetString(p1, 4, p1.Length - 4);

        var attrs = ParseScram(serverFirst);
        var serverNonce = attrs["r"];
        if (!serverNonce.StartsWith(clientNonce, StringComparison.Ordinal))
            throw new InvalidOperationException("nonce سرور با nonce ما نمی‌خواند.");
        var salt = Convert.FromBase64String(attrs["s"]);
        var iterations = int.Parse(attrs["i"]);

        var saltedPassword = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(info.Password), salt, iterations, HashAlgorithmName.SHA256, 32);
        var clientKey = HmacSha256(saltedPassword, "Client Key");
        var storedKey = SHA256.HashData(clientKey);

        var clientFinalNoProof = $"c=biws,r={serverNonce}";
        var authMessage = $"{clientFirstBare},{serverFirst},{clientFinalNoProof}";
        var clientSignature = HmacSha256(storedKey, authMessage);
        var proof = new byte[clientKey.Length];
        for (var i = 0; i < proof.Length; i++) proof[i] = (byte)(clientKey[i] ^ clientSignature[i]);

        var clientFinal = $"{clientFinalNoProof},p={Convert.ToBase64String(proof)}";
        await SendAsync((byte)'p', Encoding.UTF8.GetBytes(clientFinal), ct);

        // AuthenticationSASLFinal + AuthenticationOk
        while (true)
        {
            var (t, p) = await ReadMessageAsync(ct);
            if (t == (byte)'E') throw ReadError(p);
            if (t != (byte)'R') continue;
            var code = BinaryPrimitives.ReadInt32BigEndian(p);
            if (code == 12)
            {
                // امضای سرور را راستی‌آزمایی می‌کنیم؛ بدون این، یک سرورِ جعلی
                // می‌توانست بدون دانستن رمز، ما را بپذیرد.
                var serverFinal = ParseScram(Encoding.UTF8.GetString(p, 4, p.Length - 4));
                var serverKey = HmacSha256(saltedPassword, "Server Key");
                var expected = Convert.ToBase64String(HmacSha256(serverKey, authMessage));
                if (!serverFinal.TryGetValue("v", out var v) || !CryptographicOperations.FixedTimeEquals(
                        Encoding.UTF8.GetBytes(v), Encoding.UTF8.GetBytes(expected)))
                    throw new InvalidOperationException("امضای سرور معتبر نبود.");
                continue;
            }
            if (code == 0) return;
        }
    }

    private static byte[] HmacSha256(byte[] key, string text) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(text));

    private static Dictionary<string, string> ParseScram(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in text.Split(','))
        {
            var i = part.IndexOf('=');
            if (i > 0) map[part[..i]] = part[(i + 1)..];
        }
        return map;
    }

    private async Task WaitForReadyAsync(CancellationToken ct)
    {
        while (true)
        {
            var (type, payload) = await ReadMessageAsync(ct);
            if (type == (byte)'Z') return;
            if (type == (byte)'E') throw ReadError(payload);
        }
    }

    /* ------------------------------ پرس‌وجو ------------------------------ */

    public async Task<List<PgRow>> QueryAsync(string sql, params object?[] parameters)
    {
        var (rows, _) = await RunExtendedAsync(sql, parameters);
        return rows;
    }

    public async Task<int> ExecuteAsync(string sql, params object?[] parameters)
    {
        var (_, affected) = await RunExtendedAsync(sql, parameters);
        return affected;
    }

    /// <summary>چند دستور با ; جدا — فقط برای DDL و BEGIN/COMMIT.</summary>
    public async Task SimpleQueryAsync(string sql)
    {
        var body = new MemoryStream();
        WriteCString(body, sql);
        await SendAsync((byte)'Q', body.ToArray(), default);

        PgException? failure = null;
        while (true)
        {
            var (type, payload) = await ReadMessageAsync(default);
            if (type == (byte)'E') failure = ReadError(payload);
            else if (type == (byte)'Z') break;
        }
        if (failure is not null) throw failure;
    }

    private async Task<(List<PgRow> Rows, int Affected)> RunExtendedAsync(string sql, object?[] parameters)
    {
        // Parse → Bind → Describe → Execute → Sync، همه در یک رفت‌وبرگشت.
        var parse = new MemoryStream();
        WriteCString(parse, "");            // عبارتِ بی‌نام
        WriteCString(parse, sql);
        WriteInt16(parse, 0);               // نوعِ پارامترها را خود سرور حدس بزند
        await SendAsync((byte)'P', parse.ToArray(), default, flush: false);

        var bind = new MemoryStream();
        WriteCString(bind, "");             // پورتالِ بی‌نام
        WriteCString(bind, "");
        WriteInt16(bind, 0);                // همه‌ی پارامترها متنی
        WriteInt16(bind, (short)parameters.Length);
        foreach (var p in parameters)
        {
            if (p is null) { WriteInt32(bind, -1); continue; }
            var text = ToPgText(p);
            var bytes = Encoding.UTF8.GetBytes(text);
            WriteInt32(bind, bytes.Length);
            bind.Write(bytes);
        }
        WriteInt16(bind, 0);                // نتیجه هم متنی
        await SendAsync((byte)'B', bind.ToArray(), default, flush: false);

        var describe = new MemoryStream();
        describe.WriteByte((byte)'P');
        WriteCString(describe, "");
        await SendAsync((byte)'D', describe.ToArray(), default, flush: false);

        var exec = new MemoryStream();
        WriteCString(exec, "");
        WriteInt32(exec, 0);                // بدون سقفِ تعداد سطر
        await SendAsync((byte)'E', exec.ToArray(), default, flush: false);

        await SendAsync((byte)'S', Array.Empty<byte>(), default);

        var rows = new List<PgRow>();
        Dictionary<string, int>? index = null;
        var columns = 0;
        var affected = 0;
        PgException? failure = null;

        while (true)
        {
            var (type, payload) = await ReadMessageAsync(default);
            switch (type)
            {
                case (byte)'T':
                    (index, columns) = ReadRowDescription(payload);
                    break;

                case (byte)'D':
                    rows.Add(ReadDataRow(payload, index ??= new Dictionary<string, int>(), columns));
                    break;

                case (byte)'C':
                    affected += ParseAffected(ReadCString(payload, 0, out _));
                    break;

                case (byte)'E':
                    failure = ReadError(payload);
                    break;

                case (byte)'Z':
                    if (failure is not null) throw failure;
                    return (rows, affected);
            }
        }
    }

    /// <summary>
    /// تبدیل مقدار .NET به متنی که پستگرس می‌فهمد. عمداً کم و صریح است —
    /// چیزی که این‌جا نباشد، به‌جای تبدیلِ حدسی، خطا می‌دهد.
    /// </summary>
    private static string ToPgText(object value) => value switch
    {
        string s => s,
        bool b => b ? "t" : "f",
        int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
        long l => l.ToString(System.Globalization.CultureInfo.InvariantCulture),
        DateTime d => d.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"نوع پارامتر {value.GetType().Name} پشتیبانی نمی‌شود."),
    };

    private static int ParseAffected(string tag)
    {
        // "INSERT 0 3" / "UPDATE 2" / "DELETE 1" / "SELECT 5"
        var parts = tag.Split(' ');
        return parts.Length > 0 && int.TryParse(parts[^1], out var n) ? n : 0;
    }

    private static (Dictionary<string, int>, int) ReadRowDescription(byte[] payload)
    {
        var count = BinaryPrimitives.ReadInt16BigEndian(payload);
        var map = new Dictionary<string, int>(count, StringComparer.Ordinal);
        var pos = 2;
        for (var i = 0; i < count; i++)
        {
            var name = ReadCString(payload, pos, out pos);
            pos += 18;                       // tableOid, colNo, typeOid, typeLen, typeMod, format
            map[name] = i;
        }
        return (map, count);
    }

    private static PgRow ReadDataRow(byte[] payload, Dictionary<string, int> index, int columns)
    {
        var count = BinaryPrimitives.ReadInt16BigEndian(payload);
        var values = new string?[Math.Max(count, columns)];
        var pos = 2;
        for (var i = 0; i < count; i++)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(pos));
            pos += 4;
            if (len < 0) { values[i] = null; continue; }
            values[i] = Encoding.UTF8.GetString(payload, pos, len);
            pos += len;
        }
        return new PgRow(values, index);
    }

    private static PgException ReadError(byte[] payload)
    {
        string severity = "ERROR", code = "", message = "", detail = "";
        var pos = 0;
        while (pos < payload.Length)
        {
            var field = payload[pos++];
            if (field == 0) break;
            var text = ReadCString(payload, pos, out pos);
            switch ((char)field)
            {
                case 'S': severity = text; break;
                case 'C': code = text; break;
                case 'M': message = text; break;
                case 'D': detail = text; break;
            }
        }
        return new PgException(severity, code, message, detail);
    }

    /* ------------------------------ لایه‌ی سیم ------------------------------ */

    private async Task SendAsync(byte type, byte[] body, CancellationToken ct, bool flush = true)
    {
        var msg = new byte[5 + body.Length];
        msg[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(msg.AsSpan(1), body.Length + 4);
        body.CopyTo(msg, 5);
        try
        {
            await _stream.WriteAsync(msg, ct);
            if (flush) await _stream.FlushAsync(ct);
        }
        catch
        {
            Broken = true;
            throw;
        }
    }

    private async Task<(byte Type, byte[] Payload)> ReadMessageAsync(CancellationToken ct)
    {
        try
        {
            await ReadExactAsync(_header, 5, ct);
            var length = BinaryPrimitives.ReadInt32BigEndian(_header.AsSpan(1)) - 4;
            if (length < 0 || length > 64 * 1024 * 1024)
                throw new InvalidOperationException($"طول پیام غیرمنطقی: {length}");
            var payload = new byte[length];
            if (length > 0) await ReadExactAsync(payload, length, ct);
            return (_header[0], payload);
        }
        catch
        {
            Broken = true;
            throw;
        }
    }

    private async Task ReadExactAsync(byte[] buffer, int count, CancellationToken ct)
    {
        var read = 0;
        while (read < count)
        {
            var n = await _stream.ReadAsync(buffer.AsMemory(read, count - read), ct);
            if (n == 0) throw new IOException("اتصال به پستگرس بسته شد.");
            read += n;
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            if (_tcp is { Connected: true })
            {
                await SendAsync((byte)'X', Array.Empty<byte>(), default);
            }
        }
        catch { /* بسته شدنِ اتصال هیچ‌وقت نباید خطا بدهد */ }
        finally
        {
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Dispose(); } catch { }
        }
    }

    /* ------------------------------ کمکی‌ها ------------------------------ */

    private static void WriteInt32(MemoryStream s, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, value);
        s.Write(b);
    }

    private static void WriteInt16(MemoryStream s, short value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(b, value);
        s.Write(b);
    }

    private static void WriteCString(MemoryStream s, string value)
    {
        s.Write(Encoding.UTF8.GetBytes(value));
        s.WriteByte(0);
    }

    private static string ReadCString(byte[] buffer, int start, out int next)
    {
        var end = Array.IndexOf(buffer, (byte)0, start);
        if (end < 0) end = buffer.Length;
        next = end + 1;
        return Encoding.UTF8.GetString(buffer, start, end - start);
    }

    private static List<string> ReadCStringList(ReadOnlySpan<byte> span)
    {
        var list = new List<string>();
        var bytes = span.ToArray();
        var pos = 0;
        while (pos < bytes.Length)
        {
            var text = ReadCString(bytes, pos, out pos);
            if (text.Length == 0) break;
            list.Add(text);
        }
        return list;
    }
}
