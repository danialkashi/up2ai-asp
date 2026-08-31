#!/usr/bin/env python3
"""
از سایت در حال اجرا یک فایل HTML تک‌تکه‌ی خودبسنده می‌سازد که هم صفحه‌ی اصلی
و هم همه‌ی صفحه‌های پنل مدیریت را نشان می‌دهد.

    dotnet run            # روی پورت 5199
    python3 build-preview.py [http://localhost:5199]

چرا این کار لازم است: صفحه‌های پنل پشت ورود هستند و بدون سرور اصلاً باز
نمی‌شوند. این اسکریپت یک بار وارد می‌شود، مارک‌آپ واقعی هر صفحه را برمی‌دارد،
و همه را در یک فایل کنار هم می‌گذارد — با یک «ورود» نمایشی که فقط بین
نماها جابه‌جا می‌کند.

مهم: خروجی یک عکسِ ایستاست، نه یک کپیِ کارکننده. فرم‌ها به سروری وصل نیستند.
"""
import base64
import json
import pathlib
import re
import sys
import urllib.parse
import urllib.request
import http.cookiejar

BASE = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5199").rstrip("/")
PW = "TestAdminPass9!"          # فقط اعتبارِ تستِ همین ساخت؛ رمز واقعی نیست
DEMO_USER = "admin"
DEMO_PASS = "up2ai-preview"     # قفل نمایشیِ خود فایل پیش‌نمایش

jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def get(path: str) -> str:
    with opener.open(BASE + path, timeout=30) as r:
        return r.read().decode("utf-8")


def login() -> None:
    html = get("/admin/login")
    token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]*)"', html).group(1)
    body = urllib.parse.urlencode({"password": PW, "__RequestVerificationToken": token}).encode()
    opener.open(urllib.request.Request(BASE + "/admin/login", data=body), timeout=30)


def body_of(html: str) -> str:
    """بدنه، بدون اسکریپت‌ها و بدون توکن‌های ضدجعل (که در فایل ایستا بی‌معنی‌اند)."""
    start = html.find(">", html.find("<body")) + 1
    b = html[start:html.rfind("</body>")]
    b = re.sub(r"<script\b[^>]*>.*?</script>", "", b, flags=re.S)
    b = re.sub(r'<input[^>]*name="__RequestVerificationToken"[^>]*>', "", b)
    return b


try:
    home = get("/")
except Exception as exc:  # noqa: BLE001
    sys.exit(f"سرور روی {BASE} جواب نداد ({exc}).\nاول این را اجرا کن:  dotnet run")

# ترتیب مهم است: صفحه‌ی ورود باید *قبل از* ورود گرفته شود، وگرنه سرور
# کاربرِ واردشده را به خانه‌ی پنل می‌فرستد و همان را می‌گیریم.
login_page = body_of(get("/admin/login"))
login()
views = {
    "site": ("صفحه‌ی اصلی سایت", body_of(home)),
    "admin-login": ("پنل — صفحه‌ی ورود", login_page),
    "admin-home": ("پنل — خانه (فهرست بخش‌ها)", body_of(get("/admin"))),
    "admin-editor": ("پنل — ویرایشگر یک بخش", body_of(get("/admin/process"))),
    "admin-leads": ("پنل — صندوق لید", body_of(get("/admin/leads"))),
}

# ---------- CSS، با فونت به‌صورت data URI ----------
css = get("/css/site.css")
font_b64 = base64.b64encode(pathlib.Path("wwwroot/fonts/Vazirmatn-var.woff2").read_bytes()).decode()
css = css.replace("/fonts/Vazirmatn-var.woff2", f"data:font/woff2;base64,{font_b64}")

# ---------- داده‌ی دمو برای جاوااسکریپت صفحه ----------
demo_data = json.loads(get("/") .split('id="demo-data">')[1].split("</script>")[0]) \
    if 'id="demo-data">' in home else []

site_js = get("/js/site.js")

shell_css = """
:root { color-scheme: light }
body.pv-shell { margin:0; background:#0a0a0f }
#pv-gate { position:fixed; inset:0; z-index:9999; display:grid; place-items:center;
  background:#0a0a0f; font-family:Vazirmatn,system-ui,sans-serif }
#pv-gate form { width:min(92vw,360px); background:#fff; border-radius:16px; padding:28px }
#pv-gate h2 { margin:0 0 4px; font-size:19px; color:#18181b }
#pv-gate p { margin:0 0 18px; font-size:13px; line-height:1.9; color:#52525b }
#pv-gate label { display:block; font-size:13px; font-weight:600; color:#18181b; margin:12px 0 6px }
#pv-gate input { width:100%; box-sizing:border-box; min-height:44px; padding:0 12px; font:inherit;
  border:1px solid #e4e4e7; border-radius:12px; direction:ltr }
#pv-gate button { width:100%; min-height:44px; margin-top:18px; border:0; border-radius:12px;
  background:linear-gradient(to left,#4f46e5,#7c3aed); color:#fff; font:inherit; font-weight:600; cursor:pointer }
#pv-gate .err { margin-top:10px; font-size:13px; color:#b91c1c; min-height:18px }
#pv-gate .hint { margin-top:16px; font-size:12px; line-height:1.9; color:#52525b;
  background:#f4f4f5; border-radius:10px; padding:10px 12px }
#pv-bar { position:sticky; top:0; z-index:9000; display:flex; flex-wrap:wrap; gap:6px; align-items:center;
  padding:10px 14px; background:#111827; border-bottom:2px dashed #f59e0b;
  font:600 12.5px/1.6 Vazirmatn,system-ui,sans-serif }
#pv-bar span.t { color:#fde68a; margin-left:auto }
#pv-bar button { border:1px solid #374151; background:#1f2937; color:#e5e7eb; cursor:pointer;
  border-radius:8px; padding:7px 12px; font:inherit }
#pv-bar button[aria-current="true"] { background:#4f46e5; border-color:#6366f1; color:#fff }
.pv-view { display:none }
.pv-view.on { display:block }
"""

shell_js = """
(function(){
  var U=%s, P=%s;
  var gate=document.getElementById('pv-gate'), f=document.getElementById('pv-form');
  f.addEventListener('submit',function(e){
    e.preventDefault();
    var u=document.getElementById('pv-u').value.trim(), p=document.getElementById('pv-p').value;
    if(u===U&&p===P){ gate.remove(); document.body.classList.remove('pv-locked'); show('site'); }
    else document.getElementById('pv-err').textContent='نام کاربری یا رمز درست نیست.';
  });
  function show(id){
    document.querySelectorAll('.pv-view').forEach(function(v){ v.classList.toggle('on', v.id==='v-'+id); });
    document.querySelectorAll('#pv-bar button').forEach(function(b){ b.setAttribute('aria-current', String(b.dataset.v===id)); });
    window.scrollTo(0,0);
    if(id==='site' && window.__initSite) window.__initSite();
  }
  document.querySelectorAll('#pv-bar button').forEach(function(b){
    b.addEventListener('click',function(){ show(b.dataset.v); });
  });
})();
""" % (json.dumps(DEMO_USER), json.dumps(DEMO_PASS))

# ---------- اسپرایت لوگو: فقط یک نسخه، بیرون از همه‌ی نماها ----------
# هر صفحه یک کپی از اسپرایت دارد. اگر همه را نگه داریم، پنج المان با
# شناسه‌ی یکسان (`u2-mark`، `u2-grad`) در سند می‌ماند و `<use>` به اولی
# اشاره می‌کند — که داخل نمای پنهان است و در بعضی مرورگرها اصلاً رسم
# نمی‌شود. پس یکی را بیرون می‌کشیم و بقیه را حذف می‌کنیم.
SPRITE_RE = re.compile(r'<svg width="0" height="0".*?</svg>', re.S)
sprite = ""
for key, (label, html) in list(views.items()):
    found = SPRITE_RE.search(html)
    if found and not sprite:
        sprite = found.group(0)
    views[key] = (label, SPRITE_RE.sub("", html))

bar = "".join(
    f'<button type="button" data-v="{k}">{label}</button>'
    for k, (label, _) in views.items()
)
sections = "".join(
    f'<div class="pv-view" id="v-{k}">{html}</div>' for k, (_, html) in views.items()
)

page = f"""<!doctype html>
<html lang="fa" dir="rtl" class="h-full">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>UP2AI — پیش‌نمایش سایت و پنل مدیریت</title>
<style>{css}</style>
<style>{shell_css}</style>
</head>
<body class="pv-shell pv-locked flex min-h-full flex-col">
{sprite}
<div id="pv-gate">
  <form id="pv-form">
    <h2>پیش‌نمایش UP2AI</h2>
    <p>برای دیدن سایت و پنل مدیریت وارد شو.</p>
    <label for="pv-u">نام کاربری</label>
    <input id="pv-u" autocomplete="off" spellcheck="false">
    <label for="pv-p">رمز</label>
    <input id="pv-p" type="password" autocomplete="off">
    <button type="submit">ورود</button>
    <div class="err" id="pv-err"></div>
    <div class="hint">
      نام کاربری: <b>{DEMO_USER}</b> — رمز: <b>{DEMO_PASS}</b><br>
      این فقط قفلِ همین فایل پیش‌نمایش است و هیچ ربطی به رمز واقعی پنل ندارد.
    </div>
  </form>
</div>

<div id="pv-bar">{bar}<span class="t">پیش‌نمایش ایستا — دکمه‌ها و فرم‌ها کار نمی‌کنند</span></div>
{sections}

<script id="demo-data" type="application/json">{json.dumps(demo_data, ensure_ascii=False)}</script>
<script>window.__initSite=function(){{ {site_js} }};</script>
<script>{shell_js}</script>
</body>
</html>
"""

out = "UP2AI-preview.html"
pathlib.Path(out).write_text(page, encoding="utf-8")
print(f"{out} — {len(page)/1024:.1f} KB")
