/*
 * تعاملهای صفحهی اصلی.
 *
 * در نسخهی Next.js اینها را ریاکت انجام میداد (state Framer Motion
 * کامپوننتهای کلاینتی). اینجا سرور فقط HTML میفرستد و همین چند تابع ساده
 * کارهای تعاملی را انجام میدهند — بدون هیچ کتابخانهای بدون باندل.
 */
(function () {
  "use strict";

  function escHtml(t) {
    var d = document.createElement("div");
    d.textContent = t == null ? "" : t;
    return d.innerHTML;
  }

  function escAttr(t) {
    return escHtml(t).replace(/"/g, "&quot;");
  }

  var bar = document.querySelector("header > div");
  if (bar) {
    var hasDarkHero = !!document.getElementById("top");
    var applyHeaderTheme = function () {
      var scrolled = !hasDarkHero || window.scrollY > 8;
      bar.classList.toggle("border-white/10", scrolled);
      bar.classList.toggle("bg-ink/85", scrolled);
      bar.classList.toggle("backdrop-blur-xl", scrolled);
      bar.classList.toggle("backdrop-saturate-150", scrolled);
      bar.classList.toggle("border-transparent", !scrolled);
      bar.classList.toggle("bg-transparent", !scrolled);

      bar.style.borderBottomColor = scrolled ? "rgba(255,255,255,0.10)" : "transparent";
      bar.style.background = scrolled ? "rgba(17,18,27,0.86)" : "transparent";
      bar.style.backdropFilter = scrolled ? "blur(18px) saturate(150%)" : "none";
    };
    window.addEventListener("scroll", applyHeaderTheme, { passive: true });
    applyHeaderTheme();
  }

  var btn = document.querySelector('button[aria-controls="mobile-menu"]');
  var header = document.querySelector("header");
  if (btn && header) {
    var navLinks = header.querySelectorAll("nav ul a");
    var ctaLink = header.querySelector("a[data-cta]");
    var items = "";

    navLinks.forEach(function (a) {
      items +=
        '<li><a href="' + escAttr(a.getAttribute("href")) + '" data-pv-close ' +
        'class="block rounded-xl px-3 py-3 text-[15px] font-medium text-on-dark/85 ' +
        'transition-colors hover:bg-white/5 hover:text-white">' + escHtml(a.textContent) + "</a></li>";
    });

    if (ctaLink) {
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

    var openLabel = btn.getAttribute("aria-label") || "";
    var closeLabel = btn.getAttribute("data-label-close") || openLabel;
    var paths = btn.querySelectorAll("path");

    var setOpen = function (v) {
      document.body.classList.toggle("pv-menu-open", v);
      btn.setAttribute("aria-expanded", String(v));
      btn.setAttribute("aria-label", v ? closeLabel : openLabel);
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

    var desktop = window.matchMedia("(min-width: 768px)");
    var onBreakpoint = function (e) {
      if (e.matches && document.body.classList.contains("pv-menu-open")) setOpen(false);
    };
    if (desktop.addEventListener) desktop.addEventListener("change", onBreakpoint);
    else if (desktop.addListener) desktop.addListener(onBreakpoint);
  }

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

  var nav = document.querySelector("[data-nav-scroll]");
  if (nav) {
    var syncNavOverflow = function () {
      var over = nav.scrollWidth - nav.clientWidth > 1;
      nav.setAttribute("data-nav-scroll", over ? "overflow" : "");
    };

    syncNavOverflow();
    if ("ResizeObserver" in window) new ResizeObserver(syncNavOverflow).observe(nav);
    else window.addEventListener("resize", syncNavOverflow);

    if (document.fonts && document.fonts.ready) document.fonts.ready.then(syncNavOverflow);
  }

  var dataEl = document.getElementById("demo-data");
  var demo = document.getElementById("demo");
  if (dataEl && demo) {
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
    var played = false;

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

    var panelEl = demo.querySelector('[role="tabpanel"]');

    function lockHeight() {
      if (!panelEl) return;
      panelEl.style.minHeight = "";
      var tallest = 0;
      for (var i = 0; i < DEMO.length; i++) {
        render(i);
        tallest = Math.max(tallest, panelEl.offsetHeight);
      }
      render(idx);
      panelEl.style.minHeight = tallest + "px";
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

    var TAB_ON = ["border-brand/35", "bg-card", "text-brand-ink", "shadow-[0_6px_20px_-12px_rgba(99,102,241,0.55)]"];
    var TAB_OFF = ["border-hairline", "bg-transparent", "text-on-light-muted", "hover:bg-card", "hover:text-on-light"];

    function select(i) {
      idx = i;
      [].forEach.call(tabs, function (t, k) {
        var on = k === i;
        t.setAttribute("aria-selected", String(on));
        t.tabIndex = on ? 0 : -1;
        TAB_ON.forEach(function (c) { t.classList.toggle(c, on); });
        TAB_OFF.forEach(function (c) { t.classList.toggle(c, !on); });
      });

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

    var resizeTimer;
    var lastWidth = window.innerWidth;
    window.addEventListener("resize", function () {
      if (window.innerWidth === lastWidth) return;
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
  }
})();
