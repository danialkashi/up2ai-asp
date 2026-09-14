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
    var navLinks = header.querySelectorAll('nav a[href^="#"]');
    var ctaLink = header.querySelector('a[href="#contact"]:not(nav a)');

    var items = "";
    navLinks.forEach(function (a) {
      items +=
        '<li><a href="' + a.getAttribute("href") + '" data-pv-close ' +
        'class="block rounded-xl px-3 py-3 text-[15px] font-medium text-on-dark/85 ' +
        'transition-colors hover:bg-white/5 hover:text-white">' + a.textContent + "</a></li>";
    });
    if (ctaLink) {
      items +=
        '<li class="pt-2"><a href="#contact" data-pv-close class="block rounded-xl ' +
        'bg-gradient-to-l from-brand to-brand-2 px-4 py-3 text-center text-[15px] ' +
        'font-medium text-white">' + ctaLink.textContent.trim() + "</a></li>";
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

  if (!chatList || !actList) return;

  function esc(t) {
    var d = document.createElement("div");
    d.textContent = t;
    return d.innerHTML;
  }

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

  function play() {
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

  function select(i) {
    idx = i;
    [].forEach.call(tabs, function (t, k) {
      var on = k === i;
      t.setAttribute("aria-selected", String(on));
      t.className =
        "inline-flex items-center gap-2 rounded-xl border px-4 py-[11px] text-[14px] " +
        "font-medium transition-colors duration-200 " +
        (on
          ? "border-brand/35 bg-card text-brand-ink shadow-[0_6px_20px_-12px_rgba(99,102,241,0.55)]"
          : "border-hairline bg-transparent text-on-light-muted hover:bg-card hover:text-on-light");
    });
    render(i);
    play();
  }

  [].forEach.call(tabs, function (t, i) {
    t.addEventListener("click", function () { select(i); });
  });

  var replay = demo.querySelector('button:not([role="tab"])');
  if (replay) replay.addEventListener("click", function () { render(idx); play(); });

  render(0);
  if ("IntersectionObserver" in window) {
    var panel = demo.querySelector('[role="tabpanel"]');
    if (panel) {
      var io = new IntersectionObserver(function (es) {
        if (es[0].isIntersecting) { play(); io.disconnect(); }
      }, { threshold: 0.25 });
      io.observe(panel);
    }
  } else {
    play();
  }
})();

// ---------- Client-side validation for contact form ----------
(function () {
  'use strict';
  var form = document.getElementById('contact-form') || document.querySelector('#contact form[method="post"], #contact form');
  if (!form) return;

  var errName = form.getAttribute('data-err-name') || 'لطفاً نامتان را بنویسید.';
  var errReach = form.getAttribute('data-err-reach') || 'یک راه ارتباطی بنویسید.';
  var errNeed = form.getAttribute('data-err-need') || 'کمی بیشتر توضیح بدهید.';

  function setError(input, message) {
    input.setAttribute('aria-invalid', 'true');
    var id = input.id || ('err-' + Math.random().toString(36).slice(2));
    input.id = id;
    var existing = document.getElementById(id + '-error');
    if (!existing) {
      var p = document.createElement('p');
      p.id = id + '-error';
      p.className = 'text-[13px] text-red-700';
      p.textContent = message;
      input.insertAdjacentElement('afterend', p);
    } else {
      existing.textContent = message;
    }
  }

  function clearError(input) {
    input.removeAttribute('aria-invalid');
    var existing = document.getElementById((input.id || '') + '-error');
    if (existing) existing.remove();
  }

  // validate all fields and return true when valid
  function validateAll() {
    var name = form.querySelector('input[name="Name"]');
    var reach = form.querySelector('input[name="Reach"]');
    var need = form.querySelector('textarea[name="Need"]');
    var hasError = false;

    if (name) {
      if ((name.value || '').trim().length < 2) { setError(name, errName); hasError = true; } else { clearError(name); }
    }
    if (reach) {
      if ((reach.value || '').trim().length < 5) { setError(reach, errReach); hasError = true; } else { clearError(reach); }
    }
    if (need) {
      if ((need.value || '').trim().length < 10) { setError(need, errNeed); hasError = true; } else { clearError(need); }
    }

    return !hasError;
  }

  // Capture-phase click handler: run validation early and prevent default/navigation
  document.addEventListener('click', function (e) {
    try {
      var el = e.target;
      if (!el || !el.closest) return;
      var btn = el.closest('button, input[type="submit"], a');
      if (!btn) return;

      // If the click is on a submit control inside the contact form or an anchor to #contact,
      // validate immediately and stop propagation/default if invalid. This prevents other
      // handlers or anchor navigation from running and causing scroll.
      var isRelatedToForm = form.contains(btn);
      var isContactAnchor = btn.tagName === 'A' && (btn.getAttribute('href') || '').indexOf('#contact') === 0;
      if (isRelatedToForm || isContactAnchor) {
        var ok = validateAll();
        if (!ok) {
          e.preventDefault();
          e.stopImmediatePropagation();
          var firstInvalid = form.querySelector('[aria-invalid="true"]');
          if (firstInvalid && typeof firstInvalid.focus === 'function') firstInvalid.focus();
        }
      }
    } catch (ex) { /* silent */ }
  }, true);

  // Fallback: also validate on submit (bubble) to be safe.
  form.addEventListener('submit', function (e) {
    if (!validateAll()) {
      e.preventDefault();
      var invalid = form.querySelector('[aria-invalid="true"]');
      if (invalid && typeof invalid.focus === 'function') invalid.focus();
    }
  }, { passive: false });

  // Live clear on input
  ['input', 'change'].forEach(function (ev) {
    form.addEventListener(ev, function (e) {
      var t = e.target;
      if (!t) return;
      if (t.name === 'Name' && (t.value || '').trim().length >= 2) clearError(t);
      if (t.name === 'Reach' && (t.value || '').trim().length >= 5) clearError(t);
      if (t.name === 'Need' && (t.value || '').trim().length >= 10) clearError(t);
    });
  });
})();
