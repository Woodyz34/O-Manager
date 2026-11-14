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
  var lb = $("lightbox"), lbImg = $("lbImg");
  rail.addEventListener("click", function (e) {
    var b = e.target.closest(".g-item"); if (!b) return;
    lbImg.src = b.dataset.full; lbImg.alt = b.querySelector("img").alt;
    if (lb.showModal) lb.showModal(); else window.open(b.dataset.full);
  });
  $("lbClose").addEventListener("click", function () { lb.close(); });
  lb.addEventListener("click", function (e) { if (e.target === lb) lb.close(); });

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

  /* ---------- Download: always the newest GitHub release ---------- */
  var cfg = window.OM_CONFIG || {}, release = null, releaseState = "loading";
  var repoUrl = "https://github.com/" + cfg.releasesRepo + "/releases";
  $("dlAll").href = repoUrl;
  $("dlBtn").href = repoUrl + "/latest";
  function renderDownload() {
    var meta = $("dlMeta"), l = lang();
    if (releaseState === "ok") {
      var d = new Date(release.published_at), mb = release.asset ? (release.asset.size / 1048576).toFixed(0) + " MB · " : "";
      var date = d.getDate() + " " + months[l][d.getMonth()] + " " + d.getFullYear();
      meta.textContent = (l === "sv" ? "Version " : "Version ") + release.tag_name + " · " + mb + (l === "sv" ? "släppt " : "released ") + date;
    } else if (releaseState === "none") {
      meta.textContent = l === "sv" ? "Första versionen kommer snart." : "The first version is coming soon.";
    } else if (releaseState === "error") {
      meta.textContent = l === "sv" ? "Senaste versionen för Windows 10 / 11." : "Latest version for Windows 10 / 11.";
    }
  }
  if (cfg.releasesRepo) {
    fetch("https://api.github.com/repos/" + cfg.releasesRepo + "/releases/latest", { headers: { Accept: "application/vnd.github+json" } })
      .then(function (r) { if (r.status === 404) { releaseState = "none"; return null; } if (!r.ok) throw r; return r.json(); })
      .then(function (j) {
        if (!j) return renderDownload();
        var asset = (j.assets || []).filter(function (a) { return a.name === cfg.assetName; })[0] || (j.assets || [])[0];
        release = { tag_name: j.tag_name, published_at: j.published_at, asset: asset };
        if (asset) $("dlBtn").href = asset.browser_download_url;
        releaseState = "ok"; renderDownload();
      })
      .catch(function () { releaseState = "error"; renderDownload(); });
  }

  $("year").textContent = new Date().getFullYear();
})();
