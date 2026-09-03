/*
 * تعامل‌های صفحه‌ی اصلی.
 *
 * در نسخه‌ی Next.js این‌ها را ری‌اکت انجام می‌داد (state، Framer Motion،
 * کامپوننت‌های کلاینتی). این‌جا سرور فقط HTML می‌فرستد و همین چند تابع ساده
 * کارهای تعاملی را انجام می‌دهند — بدون هیچ کتابخانه‌ای، بدون باندل.
 *
 * اگر جاوااسکریپت خاموش باشد صفحه هنوز کامل و خواناست: منو باز نمی‌شود ولی
 * همه‌ی لینک‌ها در فوتر هستند، و دمو سناریوی اول را با انیمیشن CSS نشان
 * می‌دهد.
 */
(function () {
  "use strict";

  /* ---- کمکی: اسکیپ کردن متنی که از محتوا می‌آید ----
     هر جای این فایل که رشته‌ای وارد innerHTML می‌شود باید از این‌ها رد شود.
     متن‌ها از پنل مدیریت می‌آیند و ممکن است & یا < داشته باشند. */
  function escHtml(t) {
    var d = document.createElement("div");
    d.textContent = t == null ? "" : t;
    return d.innerHTML;
  }

  function escAttr(t) {
    return escHtml(t).replace(/"/g, "&quot;");
  }

  /* ---- پس‌زمینه‌ی هدر بعد از شروع اسکرول ---- */
  var bar = document.querySelector("header > div");
  if (bar) {
    var ON = "border-white/10 bg-ink/85 backdrop-blur-xl backdrop-saturate-150".split(" ");
    var OFF = "border-transparent bg-transparent".split(" ");
    var onScroll = function () {
      var s = window.scrollY > 8;
      ON.forEach(function (c) { bar.classList.toggle(c, s); });
      OFF.forEach(function (c) { bar.classList.toggle(c, !s); });
    };
    window.addEventListener("scroll", onScroll, { passive: true });
    onScroll();
  }

  /* ---- منوی موبایل ----
     مارک‌آپ منو در سرور رندر نمی‌شود (در نسخه‌ی ری‌اکت هم شرطی بود)، پس
     همان‌جا از روی لینک‌های منوی دسکتاپ ساخته می‌شود تا یک منبع حقیقت
     بماند: اگر آیتمی از پنل اضافه/کم شد، هر دو منو با هم عوض می‌شوند. */
  var btn = document.querySelector('button[aria-controls="mobile-menu"]');
  var header = document.querySelector("header");
  if (btn && header) {
    // فقط لینک‌های خودِ فهرستِ منو. قبلاً `nav a[href^="#"]` بود که دکمه‌ی
    // CTA را هم — چون داخل nav است — به‌عنوان یک آیتم متنیِ ساده وارد کشو
    // می‌کرد؛ نتیجه دو آیتم با یک مقصد بود («تماس» و «رزرو مشاوره رایگان»).
    var navLinks = header.querySelectorAll("nav ul a");
    // CTA با قلاب صریح، نه با حدس زدنِ href. انتخابگر قبلی
    // (`a[href="#contact"]:not(nav a)`) هیچ‌وقت چیزی برنمی‌گرداند.
    var ctaLink = header.querySelector("a[data-cta]");

    // متنِ آیتم‌ها از پنل مدیریت می‌آید، پس قبل از رفتن داخل HTML اسکیپ
    // می‌شود — وگرنه یک برچسبِ حاوی & یا < منو را خراب می‌کند.
    var items = "";
    navLinks.forEach(function (a) {
      items +=
        '<li><a href="' + escAttr(a.getAttribute("href")) + '" data-pv-close ' +
        'class="block rounded-xl px-3 py-3 text-[15px] font-medium text-on-dark/85 ' +
        'transition-colors hover:bg-white/5 hover:text-white">' + escHtml(a.textContent) + "</a></li>";
    });
    if (ctaLink) {
      // مقصد از خودِ لینک خوانده می‌شود، نه ثابتِ "#contact" — وگرنه اگر
      // مقصد CTA از پنل عوض شود، نسخه‌ی موبایلش سرِ جای قبلی می‌ماند.
      items +=
        '<li class="pt-2"><a href="' + escAttr(ctaLink.getAttribute("href")) +
        '" data-pv-close class="block rounded-xl ' +
        'bg-gradient-to-l from-brand to-brand-2 px-4 py-3 text-center text-[15px] ' +
        'font-medium text-white">' + escHtml(ctaLink.textContent.trim()) + "</a></li>";
    }

    header.insertAdjacentHTML(
      "beforeend",
      '<div id="pv-overlay" class="fixed inset-0 top-[var(--header-h)] z-40 bg-ink/60 backdrop-blur-sm md:hidden" aria-hidden="true"></div>' +
      '<div id="mobile-menu" class="absolute inset-x-0 top-[var(--header-h)] z-50 origin-top border-b border-white/10 bg-ink/95 backdrop-blur-xl md:hidden">' +
      '<ul class="mx-auto flex max-w-6xl flex-col gap-1 px-4 py-4 sm:px-6">' + items + "</ul></div>"
    );

    // برچسب‌های باز/بسته از خود دکمه خوانده می‌شوند تا متن فارسی در
    // جاوااسکریپت hard-code نشود (قانون پروژه: متن فقط از محتوا می‌آید).
    var openLabel = btn.getAttribute("aria-label") || "";
    var closeLabel = btn.getAttribute("data-label-close") || openLabel;
    var paths = btn.querySelectorAll("path");

    var setOpen = function (v) {
      document.body.classList.toggle("pv-menu-open", v);
      btn.setAttribute("aria-expanded", String(v));
      btn.setAttribute("aria-label", v ? closeLabel : openLabel);
      // شکل همبرگری ↔ ضربدر (همان مسیرهایی که BurgerIcon داشت)
      if (paths.length === 3) {
        paths[0].setAttribute("d", v ? "M5 5 L15 15" : "M3 6 H17");
        paths[1].style.opacity = v ? "0" : "1";
        paths[2].setAttribute("d", v ? "M15 5 L5 15" : "M3 14 H17");
      }
    };

    btn.addEventListener("click", function () {
      setOpen(!document.body.classList.contains("pv-menu-open"));
    });
    var overlay = document.getElementById("pv-overlay");
    if (overlay) overlay.addEventListener("click", function () { setOpen(false); });
    document.querySelectorAll("[data-pv-close]").forEach(function (a) {
      a.addEventListener("click", function () { setOpen(false); });
    });
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape") setOpen(false);
    });

    // اگر پنجره به عرضِ دسکتاپ برسد در حالی که منو باز است، ببندش. خودِ CSS
    // آن‌جا کشو را پنهان می‌کند، ولی حالتِ دکمه (aria-expanded و شکل ضربدر)
    // باید با چیزی که کاربر می‌بیند بخواند — وگرنه صفحه‌خوان می‌گوید منو باز
    // است در حالی که چیزی باز نیست.
    var desktop = window.matchMedia("(min-width: 768px)");
    var onBreakpoint = function (e) {
      if (e.matches && document.body.classList.contains("pv-menu-open")) setOpen(false);
    };
    if (desktop.addEventListener) desktop.addEventListener("change", onBreakpoint);
    else if (desktop.addListener) desktop.addListener(onBreakpoint); // سافاری قدیمی
  }

  /* ---- دکمه‌ی شناور موبایل ---- */
  var fWrap = document.querySelector('div[aria-hidden][class*="fixed"][class*="bottom-0"]');
  var fLink = fWrap && fWrap.querySelector("a");
  if (fWrap && fLink) {
    var past = false;
    var atContact = false;
    var contactEl = document.getElementById("contact");
    var applyCta = function () {
      var on = past && !atContact;
      fWrap.setAttribute("aria-hidden", String(!on));
      fLink.tabIndex = on ? 0 : -1;
      fLink.classList.toggle("pointer-events-auto", on);
      fLink.classList.toggle("translate-y-0", on);
      fLink.classList.toggle("opacity-100", on);
      fLink.classList.toggle("translate-y-4", !on);
      fLink.classList.toggle("opacity-0", !on);
    };
    window.addEventListener("scroll", function () {
      past = window.scrollY > window.innerHeight * 0.85;
      applyCta();
    }, { passive: true });
    if (contactEl && "IntersectionObserver" in window) {
      new IntersectionObserver(function (e) {
        atContact = e[0].isIntersecting;
        applyCta();
      }, { rootMargin: "0px 0px -35% 0px" }).observe(contactEl);
    }
    applyCta();
  }

  /* ---- دمو: تب‌ها، پخش پلکانی، پخش دوباره ---- */
  var dataEl = document.getElementById("demo-data");
  var demo = document.getElementById("demo");
  if (!dataEl || !demo) return;

  var DEMO;
  try { DEMO = JSON.parse(dataEl.textContent || "[]"); } catch (e) { return; }
  if (!DEMO.length) return;

  var STEP = 620;
  var tabs = demo.querySelectorAll('[role="tab"]');
  var chatList = demo.querySelector('ul[class*="flex-col"]');
  var actList = demo.querySelector("ol");
  var channelEl = demo.querySelector('[role="tabpanel"] span');
  var outcomeEl = actList && actList.parentElement.querySelector("p");
  var timers = [];
  var idx = 0;
  var played = false; // آیا دمو دست‌کم یک بار پخش شده؟ (برای بازپخش بعد از resize)

  if (!chatList || !actList) return;

  var esc = escHtml;

  function render(i) {
    var s = DEMO[i];
    chatList.innerHTML = s.chat.map(function (l) {
      var side = l.from === "agent"
        ? "self-end bg-brand/[0.07] text-on-light"
        : "self-start bg-surface-2 text-on-light";
      return '<li data-demo="in" class="pv-armed max-w-[85%] rounded-2xl px-4 py-2.5 ' +
        'text-[14px] leading-[1.9] ' + side + '">' + esc(l.text) + "</li>";
    }).join("");

    actList.innerHTML = s.actions.map(function (a) {
      return '<li data-demo="in" class="pv-armed flex gap-3 text-[13.5px] leading-[1.85] text-on-dark">' +
        '<span aria-hidden="true" class="mt-[7px] inline-block h-1.5 w-1.5 shrink-0 rounded-full bg-brand-soft"></span>' +
        "<span>" + esc(a) + "</span></li>";
    }).join("");

    if (channelEl && channelEl.lastChild) channelEl.lastChild.textContent = s.channel;
    if (outcomeEl) outcomeEl.textContent = s.outcome;
  }

  /* ---- قفلِ ارتفاع پنل ----
     سناریوها تعداد پیام و اقدامِ متفاوتی دارند، پس با هر بار عوض کردن تب
     ارتفاع کل بخش کم و زیاد می‌شد و صفحه زیر دستِ کاربر می‌پرید (اندازه‌گیری
     شده: ۵۲۰ / ۴۹۳ / ۴۳۵ پیکسل).

     به‌جای عددِ ثابت در CSS — که با هر ویرایشِ متن از پنل مدیریت غلط می‌شود —
     ارتفاع همه‌ی سناریوها یک بار اندازه گرفته و بلندترینش به‌عنوان
     `min-height` روی پنل گذاشته می‌شود. چون اندازه‌گیری به عرضِ واقعی وابسته
     است، با تغییر اندازه‌ی پنجره دوباره حساب می‌شود. */
  var panelEl = demo.querySelector('[role="tabpanel"]');

  function lockHeight() {
    if (!panelEl) return;
    panelEl.style.minHeight = "";
    var tallest = 0;
    for (var i = 0; i < DEMO.length; i++) {
      render(i);
      tallest = Math.max(tallest, panelEl.offsetHeight);
    }
    render(idx); // برگرداندن سناریوی جاری
    panelEl.style.minHeight = tallest + "px";

    // render() آیتم‌ها را `pv-armed` (یعنی نامرئی) می‌سازد و منتظرِ play()
    // می‌گذارد. اگر اندازه‌گیری بعد از پخشِ اولیه اتفاق بیفتد — مثلاً وقتی
    // کاربر پنجره را تغییر اندازه می‌دهد — دیگر کسی play() را صدا نمی‌زند
    // (IntersectionObserver یک‌بارمصرف است و disconnect شده) و پنل تا کلیکِ
    // بعدی خالی می‌ماند. پس هر بار که قبلاً پخش شده، دوباره پخش می‌کنیم.
    if (played) play();
  }

  function play() {
    played = true;
    timers.forEach(clearTimeout);
    timers = [];
    var msgs = chatList.querySelectorAll("li");
    var acts = actList.querySelectorAll("li");
    [].forEach.call(msgs, function (el, i) {
      el.classList.remove("pv-on");
      timers.push(setTimeout(function () { el.classList.add("pv-on"); }, i * STEP));
    });
    [].forEach.call(acts, function (el, i) {
      el.classList.remove("pv-on");
      timers.push(setTimeout(function () { el.classList.add("pv-on"); }, i * STEP + STEP * 0.55));
    });
  }

  // فقط کلاس‌های *حالت* عوض می‌شوند، نه کل className.
  //
  // قبلاً این تابع className را از نو می‌نوشت و در نتیجه هر کلاسی که در
  // _AgentDemo.cshtml روی تب‌ها بود (مثل w-full موبایل) با اولین کلیک پاک
  // می‌شد. ضمناً آن فهرستِ کلاس باید دستی با فایل .cshtml هم‌گام می‌ماند.
  var TAB_ON = ["border-brand/35", "bg-card", "text-brand-ink", "shadow-[0_6px_20px_-12px_rgba(99,102,241,0.55)]"];
  var TAB_OFF = ["border-hairline", "bg-transparent", "text-on-light-muted", "hover:bg-card", "hover:text-on-light"];

  function select(i) {
    idx = i;
    [].forEach.call(tabs, function (t, k) {
      var on = k === i;
      t.setAttribute("aria-selected", String(on));
      // tabindex چرخشی: طبق الگوی استانداردِ tablist فقط تبِ فعال با Tab
      // گرفته می‌شود و بین تب‌ها با کلیدهای جهت‌دار حرکت می‌کنیم.
      t.tabIndex = on ? 0 : -1;
      TAB_ON.forEach(function (c) { t.classList.toggle(c, on); });
      TAB_OFF.forEach(function (c) { t.classList.toggle(c, !on); });
    });

    // شناسه‌ی پنل باید همراه تبِ فعال جابه‌جا شود، وگرنه aria-controlsِ دو تبِ
    // دیگر به شناسه‌ای اشاره می‌کند که در صفحه وجود ندارد و صفحه‌خوان پنل را
    // همیشه به نام تبِ اول می‌خواند. (سرور فقط پنلِ سناریوی اول را رندر
    // می‌کند، پس فقط یک شناسه در صفحه هست.)
    if (panelEl && tabs[i]) {
      var wanted = tabs[i].getAttribute("aria-controls");
      if (wanted) panelEl.id = wanted;
      panelEl.setAttribute("aria-labelledby", tabs[i].id);
    }

    render(i);
    play();
  }

  [].forEach.call(tabs, function (t, i) {
    t.addEventListener("click", function () { select(i); });
    // حرکت با کلیدهای جهت‌دار داخل نوار تب — انتظارِ استانداردِ یک tablist.
    // صفحه راست‌به‌چپ است، پس «راست» یعنی تبِ قبلی و «چپ» یعنی تبِ بعدی.
    t.addEventListener("keydown", function (e) {
      var last = tabs.length - 1;
      var to = null;
      if (e.key === "ArrowLeft") to = i === last ? 0 : i + 1;
      else if (e.key === "ArrowRight") to = i === 0 ? last : i - 1;
      else if (e.key === "Home") to = 0;
      else if (e.key === "End") to = last;
      if (to === null) return;
      e.preventDefault();
      select(to);
      tabs[to].focus();
    });
  });

  var replay = demo.querySelector('button:not([role="tab"])');
  if (replay) replay.addEventListener("click", function () { render(idx); play(); });

  render(0);
  lockHeight();

  // عرضِ پنل که عوض شود ارتفاعِ لازم هم عوض می‌شود؛ با تأخیر تا هنگام
  // کشیدنِ لبه‌ی پنجره ده‌ها بار محاسبه نشود.
  var resizeTimer;
  var lastWidth = window.innerWidth;
  window.addEventListener("resize", function () {
    if (window.innerWidth === lastWidth) return; // روی موبایل، باز/بسته شدن نوار مرورگر
    lastWidth = window.innerWidth;
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(lockHeight, 200);
  });

  if ("IntersectionObserver" in window) {
    if (panelEl) {
      var io = new IntersectionObserver(function (es) {
        if (es[0].isIntersecting) { play(); io.disconnect(); }
      }, { threshold: 0.25 });
      io.observe(panelEl);
    }
  } else {
    play();
  }
})();

/* ───────────────────────── سرریزِ منوی هدر ─────────────────────────
   تعداد آیتم‌های منو از پنل مدیریت تغییر می‌کند، پس هیچ عددِ ثابتی نمی‌تواند
   بگوید «چند تا جا می‌شود». به‌جای حدس، خودِ مرورگر را می‌پرسیم: اگر عرضِ
   محتوا از عرضِ ظرف بیشتر شد، ویژگی data-nav-scroll="overflow" گذاشته
   می‌شود و CSS لبه‌ها را محو می‌کند.

   بدون این، منو در حالت سرریز بی‌هیچ نشانه‌ای بریده می‌شد و کاربر نمی‌فهمید
   آیتم بیشتری هم هست. */
(function () {
  var nav = document.querySelector("[data-nav-scroll]");
  if (!nav) return;

  var sync = function () {
    // یک پیکسل رواداری: زیرپیکسل‌های چیدمان نباید سرریزِ کاذب بسازند.
    var over = nav.scrollWidth - nav.clientWidth > 1;
    nav.setAttribute("data-nav-scroll", over ? "overflow" : "");
  };

  sync();
  if ("ResizeObserver" in window) new ResizeObserver(sync).observe(nav);
  else window.addEventListener("resize", sync);

  // فونت که دیر برسد عرضِ متن عوض می‌شود — یک بار دیگر می‌سنجیم.
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(sync);
})();
