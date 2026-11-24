/* O-Manager website: small, dependency-free behaviour. */
(function () {
  "use strict";
  var doc = document.documentElement;
  var reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  var $ = function (id) { return document.getElementById(id); };
  var lang = function () { return doc.lang === "en" ? "en" : "sv"; };

  /* ---------- Language toggle ---------- */
  $("lang").addEventListener("click", function () {
    var next = lang() === "sv" ? "en" : "sv";
    doc.lang = next;
    try { localStorage.setItem("om-lang", next); } catch (e) {}
    renderNews();
    renderDownload();
    requestAnimationFrame(layoutCourse);
  });

  /* ---------- Nav: solid after scrolling, mobile menu ---------- */
  var nav = $("nav"), burger = $("burger");
  var onScroll = function () { nav.classList.toggle("solid", window.scrollY > 40); };
  window.addEventListener("scroll", onScroll, { passive: true });
  onScroll();
  burger.addEventListener("click", function () {
    var open = nav.classList.toggle("open");
    burger.setAttribute("aria-expanded", open);
  });
  $("links").addEventListener("click", function (e) {
    if (e.target.closest("a")) { nav.classList.remove("open"); burger.setAttribute("aria-expanded", "false"); }
  });

  /* ---------- Hero video: start only after the page has loaded, and not for data savers ---------- */
  var video = $("heroVideo");
  var saveData = navigator.connection && (navigator.connection.saveData || /2g/.test(navigator.connection.effectiveType || ""));
  // Phones keep the still picture: saves data and battery
  if (!reduce && !saveData && window.matchMedia("(min-width: 700px)").matches) {
    var startVideo = function () {
      video.querySelectorAll("source").forEach(function (s) { s.src = s.dataset.src; });
      video.load();
      var p = video.play(); if (p && p.catch) p.catch(function () {});
    };
    if (document.readyState === "complete") setTimeout(startVideo, 150);
    else window.addEventListener("load", function () { setTimeout(startVideo, 150); });
    // Pause when the hero is off screen to save battery
    new IntersectionObserver(function (es) {
      es.forEach(function (e) { if (!video.currentSrc) return; e.isIntersecting ? video.play().catch(function () {}) : video.pause(); });
    }).observe(video);
  }

  /* ---------- Hero cards: fade in, tilt with the mouse ---------- */
  var cards = $("heroCards");
  requestAnimationFrame(function () { cards.classList.add("in"); });
  var mid = cards.querySelector(".c-mid");
  if (!reduce && window.matchMedia("(hover: hover)").matches) {
    var raf = 0;
    window.addEventListener("pointermove", function (e) {
      if (raf) return;
      raf = requestAnimationFrame(function () {
        raf = 0;
        var r = cards.getBoundingClientRect();
        if (r.bottom < 0) return;
        var x = (e.clientX - (r.left + r.width / 2)) / window.innerWidth;
        var y = (e.clientY - (r.top + r.height / 2)) / window.innerHeight;
        mid.style.transform = "rotateY(" + (x * 26).toFixed(2) + "deg) rotateX(" + (-y * 20).toFixed(2) + "deg)";
      });
    }, { passive: true });
  }

  /* ---------- Reveal on scroll + count-up numbers ---------- */
  var countUp = function (el) {
    var end = +el.dataset.count, suffix = el.dataset.suffix || "", t0 = null;
    if (reduce) { el.textContent = end + suffix; return; }
    var step = function (t) {
      if (!t0) t0 = t;
      var k = Math.min(1, (t - t0) / 1400), e = 1 - Math.pow(1 - k, 3);
      el.textContent = Math.round(end * e) + suffix;
      if (k < 1) requestAnimationFrame(step);
    };
    el.textContent = "0" + suffix;
    requestAnimationFrame(step);
  };
  var io = new IntersectionObserver(function (es) {
    es.forEach(function (e) {
      if (!e.isIntersecting) return;
      e.target.classList.add("in");
      var c = e.target.querySelector("[data-count]");
      if (c) countUp(c);
      io.unobserve(e.target);
    });
  }, { rootMargin: "0px 0px -8% 0px", threshold: 0.12 });
  document.querySelectorAll(".reveal").forEach(function (el) { io.observe(el); });

  /* ---------- Course line: purple line from start to finish, drawn while scrolling ---------- */
  var course = $("course"), svg = $("courseLine"), path = $("coursePath"), pathLen = 0;
  function layoutCourse() {
    var box = course.getBoundingClientRect();
    var pts = Array.prototype.map.call(course.querySelectorAll(".marker svg"), function (m) {
      var r = m.getBoundingClientRect();
      return { x: r.left - box.left + r.width / 2, y: r.top - box.top + r.height / 2, r: r.width / 2 * 0.62 };
    });
    if (pts.length < 2) return;
    svg.setAttribute("viewBox", "0 0 " + box.width + " " + box.height);
    var d = "";
    for (var i = 0; i < pts.length - 1; i++) {
      var a = pts[i], b = pts[i + 1], dy = b.y - a.y;
      // Start and end outside the marker rings, like a real course line
      var y1 = a.y + a.r + 8, y2 = b.y - b.r - 8;
      var bend = (i % 2 ? -1 : 1) * Math.min(60, box.width * 0.05);
      d += "M" + a.x + " " + y1 + " C" + (a.x + bend) + " " + (a.y + dy * 0.35) + " " + (b.x - bend) + " " + (a.y + dy * 0.65) + " " + b.x + " " + y2 + " ";
    }
    path.setAttribute("d", d);
    pathLen = path.getTotalLength();
    path.style.strokeDasharray = pathLen;
    drawCourse();
  }
  function drawCourse() {
    if (!pathLen) return;
    if (reduce) { path.style.strokeDashoffset = 0; return; }
    var r = course.getBoundingClientRect(), vh = window.innerHeight;
    var k = Math.max(0, Math.min(1, (vh * 0.6 - r.top) / r.height));
    path.style.strokeDashoffset = pathLen * (1 - k);
  }
  window.addEventListener("scroll", function () { requestAnimationFrame(drawCourse); }, { passive: true });
  var rt; window.addEventListener("resize", function () { clearTimeout(rt); rt = setTimeout(layoutCourse, 120); });
  if (document.fonts && document.fonts.ready) document.fonts.ready.then(layoutCourse);
  window.addEventListener("load", layoutCourse);
  layoutCourse();

  /* ---------- Gallery rail + lightbox ---------- */
  var rail = $("rail");
  var railStep = function (dir) {
    var item = rail.querySelector(".g-item");
    rail.scrollBy({ left: dir * (item.getBoundingClientRect().width + 20), behavior: reduce ? "auto" : "smooth" });
  };
  $("railPrev").addEventListener("click", function () { railStep(-1); });
  $("railNext").addEventListener("click", function () { railStep(1); });

  /* ---------- Picture viewer: click any screenshot to see it big, zoom and pan ---------- */
  var vw = $("viewer"), stage = $("vwStage"), vImg = $("vwImg");
  var shots = Array.prototype.slice.call(document.querySelectorAll(".shot img, .tv-frame img, .tv-strip img, .g-item img"));
  var badge = '<span class="zoom-badge" aria-hidden="true"><svg viewBox="0 0 24 24"><circle cx="11" cy="11" r="6.5"/><path d="m16 16 4.5 4.5M11 8v6M8 11h6"/></svg></span>';
  shots.forEach(function (img, i) {
    var box = img.closest(".g-item") || img.closest("figure") || img.closest(".tv-frame");
    box.setAttribute("data-zoomable", "");
    box.insertAdjacentHTML("beforeend", badge);
    if (!img.closest("button")) {
      img.tabIndex = 0;
      img.setAttribute("role", "button");
      img.addEventListener("keydown", function (e) { if (e.key === "Enter" || e.key === " ") { e.preventDefault(); openViewer(i); } });
    }
    box.addEventListener("click", function (e) { e.preventDefault(); openViewer(i); });
  });
  var fullSrc = function (img) { return img.getAttribute("src").replace(/-[sl]\.webp/, "-x.webp"); };
  var caption = function (img) {
    var l = lang(), q = '[data-l="' + l + '"]';
    var c = img.closest(".g-item, figure") && (img.closest(".g-item") || img.closest("figure")).querySelector(".g-item > " + q + ", figcaption " + q);
    if (!c) { var part = img.closest(".leg, .tv-in, .duo-in, .run3d, .contests"); c = part && part.querySelector("h3 " + q + ", h2 " + q); }
    return c ? c.textContent : img.alt;
  };

  var cur = 0, k = 1, tx = 0, ty = 0, MAXK = 6;
  // Where the picture sits before zoom (offsets ignore the transform; the stage covers the whole window)
  var base = function () { return { x: vImg.offsetLeft, y: vImg.offsetTop, w: vImg.offsetWidth, h: vImg.offsetHeight }; };
  function apply(anim) {
    vImg.classList.toggle("anim", !!anim);
    vImg.style.transform = "translate(" + tx + "px," + ty + "px) scale(" + k + ")";
    $("vwZoom").textContent = Math.round(k * 100) + "%";
    var z = k > 1.01;
    stage.classList.toggle("zoomed", z); vw.classList.toggle("is-zoomed", z);
    $("vwOut").disabled = !z; $("vwIn").disabled = k >= MAXK;
  }
  function clamp() {
    var b = base(), W = window.innerWidth, H = window.innerHeight, sw = b.w * k, sh = b.h * k;
    tx = sw <= W ? (W - sw) / 2 - b.x : Math.min(-b.x, Math.max(W - sw - b.x, tx));
    ty = sh <= H ? (H - sh) / 2 - b.y : Math.min(-b.y, Math.max(H - sh - b.y, ty));
  }
  function zoomAt(nk, cx, cy, anim) {
    nk = Math.max(1, Math.min(MAXK, nk));
    var b = base(), px = (cx - b.x - tx) / k, py = (cy - b.y - ty) / k;
    tx = cx - b.x - px * nk; ty = cy - b.y - py * nk; k = nk;
    if (k === 1) { tx = 0; ty = 0; } else clamp();
    apply(anim);
  }
  function reset(anim) { k = 1; tx = 0; ty = 0; apply(anim); }
  function show(i) {
    cur = (i + shots.length) % shots.length;
    var img = shots[cur];
    reset(false);
    vImg.classList.remove("pop"); void vImg.offsetWidth; vImg.classList.add("pop");
    vImg.src = img.currentSrc || img.src;           // the small one is already loaded: show it at once
    var big = new Image(), want = fullSrc(img);
    big.onload = function () { if (shots[cur] === img) vImg.src = want; };
    big.src = want;
    vImg.alt = img.alt;
    $("vwCap").textContent = caption(img);
    $("vwCount").textContent = (cur + 1) + " / " + shots.length;
  }
  function openViewer(i) {
    show(i);
    if (vw.showModal) { vw.showModal(); vw.focus(); document.documentElement.style.overflow = "hidden"; }
    else window.open(fullSrc(shots[i]));
  }
  function closeViewer() { vw.close(); }
  vw.addEventListener("close", function () { document.documentElement.style.overflow = ""; vImg.removeAttribute("src"); });
  $("vwClose").addEventListener("click", closeViewer);
  $("vwPrev").addEventListener("click", function () { show(cur - 1); });
  $("vwNext").addEventListener("click", function () { show(cur + 1); });
  var center = function () { return [window.innerWidth / 2, window.innerHeight / 2]; };
  $("vwIn").addEventListener("click", function () { var m = center(); zoomAt(k * 1.6, m[0], m[1], true); });
  $("vwOut").addEventListener("click", function () { var m = center(); zoomAt(k / 1.6, m[0], m[1], true); });
  vw.addEventListener("keydown", function (e) {
    if (e.key === "ArrowLeft") show(cur - 1);
    else if (e.key === "ArrowRight") show(cur + 1);
    else if (e.key === "+" || e.key === "=") $("vwIn").click();
    else if (e.key === "-") $("vwOut").click();
  });
  stage.addEventListener("wheel", function (e) {
    e.preventDefault();
    zoomAt(k * Math.exp(-e.deltaY * (e.deltaMode ? 0.05 : 0.0018)), e.clientX, e.clientY, false);
  }, { passive: false });
  stage.addEventListener("dblclick", function (e) { k > 1.01 ? reset(true) : zoomAt(2.5, e.clientX, e.clientY, true); });

  // Mouse drag, touch pan, pinch, swipe between pictures, tap outside to close
  var pts = {}, start = null, moved = false, lastTap = 0;
  var list = function () { return Object.keys(pts).map(function (id) { return pts[id]; }); };
  stage.addEventListener("pointerdown", function (e) {
    stage.setPointerCapture(e.pointerId);
    pts[e.pointerId] = { x: e.clientX, y: e.clientY };
    var p = list();
    moved = false;
    start = p.length === 2
      ? { k: k, d: Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y), tx: tx, ty: ty, mx: (p[0].x + p[1].x) / 2, my: (p[0].y + p[1].y) / 2 }
      : { x: e.clientX, y: e.clientY, tx: tx, ty: ty };
  });
  stage.addEventListener("pointermove", function (e) {
    if (!pts[e.pointerId] || !start) return;
    pts[e.pointerId] = { x: e.clientX, y: e.clientY };
    var p = list();
    if (p.length === 2 && start.d) {
      var d = Math.hypot(p[0].x - p[1].x, p[0].y - p[1].y), mx = (p[0].x + p[1].x) / 2, my = (p[0].y + p[1].y) / 2;
      k = start.k; tx = start.tx + (mx - start.mx); ty = start.ty + (my - start.my);
      zoomAt(start.k * d / start.d, mx, my, false);
      moved = true;
    } else if (p.length === 1 && start.x != null) {
      var dx = e.clientX - start.x, dy = e.clientY - start.y;
      if (Math.abs(dx) + Math.abs(dy) > 6) moved = true;
      if (k > 1.01) { tx = start.tx + dx; ty = start.ty + dy; clamp(); apply(false); stage.classList.add("dragging"); }
    }
  });
  function up(e) {
    if (!pts[e.pointerId]) return;
    var wasPinch = list().length === 2;
    delete pts[e.pointerId];
    stage.classList.remove("dragging");
    if (wasPinch) { start = null; return; }
    if (!start || start.x == null) return;
    var dx = e.clientX - start.x, dy = e.clientY - start.y;
    if (k <= 1.01 && Math.abs(dx) > 60 && Math.abs(dx) > Math.abs(dy) * 1.5) show(cur + (dx < 0 ? 1 : -1));
    else if (!moved && e.pointerType !== "mouse") {
      var now = Date.now();                          // double tap = zoom, like on a phone photo
      if (now - lastTap < 300) { k > 1.01 ? reset(true) : zoomAt(2.5, e.clientX, e.clientY, true); lastTap = 0; }
      else { lastTap = now; if (e.target === stage && k <= 1.01) setTimeout(function () { if (lastTap === now) closeViewer(); }, 310); }
    } else if (!moved && e.target === stage && k <= 1.01) closeViewer();
    start = null;
  }
  stage.addEventListener("pointerup", up);
  stage.addEventListener("pointercancel", up);
  window.addEventListener("resize", function () { if (vw.open) reset(false); });

  /* ---------- News (from news.json) ---------- */
  var news = [], newsAll = false, NEWS_FIRST = 5;
  var months = {
    sv: ["jan", "feb", "mar", "apr", "maj", "jun", "jul", "aug", "sep", "okt", "nov", "dec"],
    en: ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"]
  };
  var tags = { sv: { "new": "Nytt", update: "Uppdatering", fix: "Fix" }, en: { "new": "New", update: "Update", fix: "Fix" } };
  var esc = function (s) { return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) { return "&#" + c.charCodeAt(0) + ";"; }); };
  var pick = function (v) { return v && typeof v === "object" ? (v[lang()] || v.sv || v.en) : v; };
  function renderNews() {
    var list = $("newsList"); if (!news.length) return;
    var l = lang(), shown = newsAll ? news : news.slice(0, NEWS_FIRST);
    list.innerHTML = shown.map(function (n, i) {
      var d = n.date.split("-"), tag = n.tag || "new";
      return '<article class="news-item' + (i === 0 ? " feature" : "") + '" style="animation-delay:' + (i * 60) + 'ms">' +
        '<div class="news-date"><b>' + (+d[2]) + '</b><span>' + months[l][+d[1] - 1] + " " + d[0] + "</span></div>" +
        "<div><span class=\"news-tag " + esc(tag) + "\">" + esc(tags[l][tag] || tag) + "</span>" +
        "<h3>" + esc(pick(n.title)) + "</h3><p>" + esc(pick(n.text)) + "</p></div></article>";
    }).join("");
    $("newsMore").hidden = newsAll || news.length <= NEWS_FIRST;
  }
  $("newsMore").addEventListener("click", function () { newsAll = true; renderNews(); });
  fetch("news.json", { cache: "no-cache" }).then(function (r) { return r.json(); }).then(function (j) {
    // Newest date first; same date keeps the order in the file
    news = j.map(function (n, i) { return [n, i]; }).sort(function (a, b) { return a[0].date < b[0].date ? 1 : a[0].date > b[0].date ? -1 : a[1] - b[1]; }).map(function (x) { return x[0]; });
    renderNews();
  }).catch(function () {
    $("newsList").innerHTML = '<p class="sec-lead">' + (lang() === "sv" ? "Nyheterna kunde inte laddas just nu." : "News could not be loaded right now.") + "</p>";
  });

  /* ---------- Download: always the newest GitHub release, for the visitor's computer ---------- */
  var cfg = window.OM_CONFIG || {}, release = null, releaseState = "loading";
  var repoUrl = "https://github.com/" + cfg.releasesRepo + "/releases";
  var osNames = { win: "Windows", mac: "Mac", linux: "Linux" };
  var osFiles = { win: cfg.assetName, mac: cfg.macAssetName, linux: cfg.linuxAssetName };
  var osIcons = {
    win: '<path d="M3 5.5 10 4.5v7H3Zm8-1.1L21 3v8.5H11ZM3 12.5h7v7L3 18.5Zm8 0h10V21l-10-1.4Z" class="f"/>',
    other: '<path d="M12 3v12m0 0-5-5m5 5 5-5M4 19h16" />'
  };
  var ua = navigator.userAgent || "";
  var os = /Android|iPhone|iPad|iPod/.test(ua) ? "win" : /Mac/.test(ua) ? "mac" : /Linux|X11|CrOS/.test(ua) ? "linux" : "win";
  function assetFor(o) {
    if (!release) return null;
    return release.assets.filter(function (a) { return a.name === osFiles[o]; })[0] || null;
  }
  function renderDownload() {
    var meta = $("dlMeta"), l = lang(), btn = $("dlBtn"), asset = assetFor(os);
    document.querySelectorAll(".dl-os").forEach(function (e) { e.textContent = osNames[os]; });
    $("dlIcon").innerHTML = os === "win" ? osIcons.win : osIcons.other;
    document.querySelectorAll(".dl-steps").forEach(function (e) { e.hidden = e.getAttribute("data-os") !== os; });
    // "Another computer?" only offers the other two systems
    document.querySelectorAll("#dlOther button").forEach(function (b) { b.hidden = b.getAttribute("data-os") === os; });
    btn.href = asset ? asset.browser_download_url : os === "win" && releaseState !== "ok" ? repoUrl + "/latest" : "#download";
    if (releaseState === "ok" && asset) {
      var d = new Date(release.published_at), mb = (asset.size / 1048576).toFixed(0) + " MB · ";
      var date = d.getDate() + " " + months[l][d.getMonth()] + " " + d.getFullYear();
      meta.textContent = "Version " + release.tag_name + " · " + mb + (l === "sv" ? "släppt " : "released ") + date;
    } else if (releaseState === "ok") {
      meta.textContent = l === "sv" ? osNames[os] + "-versionen kommer snart." : "The " + osNames[os] + " version is coming soon.";
    } else if (releaseState === "none") {
      meta.textContent = l === "sv" ? "Första versionen kommer snart." : "The first version is coming soon.";
    } else if (releaseState === "error") {
      meta.textContent = l === "sv" ? "Senaste versionen för " + osNames[os] + "." : "Latest version for " + osNames[os] + ".";
    }
    // Once there are Mac and Linux files, the hero says so too
    if (release && assetFor("mac") && assetFor("linux")) {
      $("heroMeta").innerHTML = '<span data-l="sv">Windows, Mac och Linux · Spela själv eller med dina vänner</span><span data-l="en">Windows, Mac and Linux · Play alone or with your friends</span>';
    }
  }
  $("dlOther").addEventListener("click", function (e) {
    var b = e.target.closest("button");
    if (b) { os = b.getAttribute("data-os"); renderDownload(); }
  });
  $("dlBtn").addEventListener("click", function (e) {
    if ($("dlBtn").getAttribute("href") === "#download") e.preventDefault();
  });
  renderDownload();
  if (cfg.releasesRepo) {
    fetch("https://api.github.com/repos/" + cfg.releasesRepo + "/releases/latest", { headers: { Accept: "application/vnd.github+json" } })
      .then(function (r) { if (r.status === 404) { releaseState = "none"; return null; } if (!r.ok) throw r; return r.json(); })
      .then(function (j) {
        if (!j) return renderDownload();
        release = { tag_name: j.tag_name, published_at: j.published_at, assets: j.assets || [] };
        releaseState = "ok"; renderDownload();
      })
      .catch(function () { releaseState = "error"; renderDownload(); });
  }

  /* ---------- Run it yourself: step into the picture while scrolling, head bob while moving ---------- */
  var r3 = document.querySelector(".r3-view");
  if (r3 && !reduce) {
    var bobT, r3Tick = function () {
      var r = r3.getBoundingClientRect(), vh = window.innerHeight;
      var p = Math.max(0, Math.min(1, (vh - r.top) / (vh * 0.75 + r.height * 0.35)));
      r3.parentNode.parentNode.style.setProperty("--p", p.toFixed(3));
      if (p > 0.7) r3.classList.add("hints");
      return r.bottom > 0 && r.top < vh;
    };
    window.addEventListener("scroll", function () {
      requestAnimationFrame(r3Tick);
      if (r3.getBoundingClientRect().top < window.innerHeight) {
        r3.classList.add("running");
        clearTimeout(bobT); bobT = setTimeout(function () { r3.classList.remove("running"); }, 180);
      }
    }, { passive: true });
    r3Tick();
  } else if (r3) r3.classList.add("hints");

  /* Endless picture reel: a copy of the pictures follows the originals; clicking a copy opens the original */
  var track = $("r3Track");
  if (track && !reduce) {
    Array.prototype.slice.call(track.children).forEach(function (f) {
      var c = f.cloneNode(true);
      c.setAttribute("aria-hidden", "true");
      var im = c.querySelector("img"); im.removeAttribute("tabindex"); im.removeAttribute("role"); im.alt = "";
      c.addEventListener("click", function () { f.click(); });
      track.appendChild(c);
    });
  }

  /* ---------- Contest clocks: time left of today's and this week's contest (Swedish time) ---------- */
  var clockDay = $("clockDay"), clockWeek = $("clockWeek");
  if (clockDay) {
    var swe = new Intl.DateTimeFormat("en-GB", { timeZone: "Europe/Stockholm", weekday: "short", hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23" });
    var days = { Mon: 0, Tue: 1, Wed: 2, Thu: 3, Fri: 4, Sat: 5, Sun: 6 };
    var two = function (n) { return (n < 10 ? "0" : "") + n; };
    var paint = function (el, text) {
      var old = el.textContent, html = "";
      for (var i = 0; i < text.length; i++) html += '<span' + (old.length === text.length && old[i] !== text[i] && !reduce ? ' class="tick"' : "") + '>' + text[i] + "</span>";
      if (old !== text) el.innerHTML = html;
    };
    var clocks = function () {
      var parts = {};
      swe.formatToParts(new Date()).forEach(function (x) { parts[x.type] = x.value; });
      var left = 86400 - (+parts.hour * 3600 + +parts.minute * 60 + +parts.second);
      paint(clockDay, two(Math.floor(left / 3600)) + ":" + two(Math.floor(left / 60) % 60) + ":" + two(left % 60));
      var w = (6 - (days[parts.weekday] || 0)) * 86400 + left;
      paint(clockWeek, Math.floor(w / 86400) + " d " + two(Math.floor(w / 3600) % 24) + " h " + two(Math.floor(w / 60) % 60) + " min");
    };
    clocks(); setInterval(clocks, 1000);
  }

  $("year").textContent = new Date().getFullYear();
})();
