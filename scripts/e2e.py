#!/usr/bin/env python3
"""
یک تست E2E بسیار ساده: صفحه لاگین مدیریت را باز می‌کند، کپچا را خوانده
و با رمز تست لاگین می‌کند، سپس بررسی می‌کند که به /admin ریدایرکت می‌شود.

این تست به dotnet run نیاز دارد که برنامه روی http://localhost:5065 اجرا شده باشد.
"""
import sys
import re
import urllib.request
import urllib.parse
import http.cookiejar
import base64

BASE = 'http://localhost:5065'
PW = 'TestAdminPass9!'

jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def get(path):
    with opener.open(BASE + path, timeout=10) as r:
        return r.read().decode('utf-8')


def post(path, data):
    body = urllib.parse.urlencode(data).encode()
    req = urllib.request.Request(BASE + path, data=body)
    with opener.open(req, timeout=10) as r:
        return r.geturl(), r.read().decode('utf-8')


try:
    html = get('/admin/login')
except Exception as e:
    print('ERROR: cannot reach app at', BASE, e)
    sys.exit(2)

m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]*)"', html)
if not m:
    print('ERROR: antiforgery token not found')
    sys.exit(2)
anti = m.group(1)

# extract captcha token and derive answer if present
cap_m = re.search(r'name="captcha_token"[^>]*value="([^"]*)"', html)
if cap_m:
    token = cap_m.group(1)
    payload_b64 = token.split('.')[0]
    padding = '=' * ((4 - len(payload_b64) % 4) % 4)
    try:
        payload = base64.urlsafe_b64decode(payload_b64 + padding)
        answer = payload.decode().split(':')[0]
    except Exception as e:
        print('ERROR: cannot decode captcha token', e)
        sys.exit(2)
else:
    token = None
    answer = None

post_data = {'password': PW, '__RequestVerificationToken': anti}
if token:
    post_data['captcha_answer'] = answer
    post_data['captcha_token'] = token

url, body = post('/admin/login', post_data)
if url.endswith('/admin') or url.endswith('/admin/'):
    print('OK: logged in and reached /admin')
    sys.exit(0)
else:
    print('FAILED: login did not reach /admin, final URL:', url)
    sys.exit(1)
