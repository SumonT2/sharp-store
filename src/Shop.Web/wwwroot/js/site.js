// Progressive enhancements. Every page also works without JavaScript (filters are plain links and forms).
(function () {
    "use strict";

    var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    // Keep sticky offsets in sync with the real header height (it wraps onto two rows on phones).
    var header = document.querySelector(".site-header");
    function syncHeaderHeight() {
        if (header) document.documentElement.style.setProperty("--header-h", header.offsetHeight + "px");
    }
    syncHeaderHeight();
    window.addEventListener("resize", syncHeaderHeight);

    // Submit a form as soon as a select changes (sort order, cart quantity). requestSubmit fires the
    // submit event, so the listing can handle it in place.
    document.addEventListener("change", function (e) {
        var el = e.target;
        if (el.matches && el.matches("[data-autosubmit]") && el.form) {
            if (el.form.requestSubmit) el.form.requestSubmit(); else el.form.submit();
        }
    });

    // Dropdowns (<details data-close-outside>): only one open at a time; close on outside click or Escape.
    document.addEventListener("toggle", function (e) {
        var d = e.target;
        if (!(d instanceof HTMLDetailsElement) || !d.open || !d.hasAttribute("data-close-outside")) return;
        document.querySelectorAll("details[data-close-outside][open]").forEach(function (other) {
            if (other !== d) other.open = false;
        });
    }, true);
    document.addEventListener("click", function (e) {
        document.querySelectorAll("details[data-close-outside][open]").forEach(function (d) {
            if (!d.contains(e.target)) d.open = false;
        });
    });
    document.addEventListener("keydown", function (e) {
        if (e.key !== "Escape") return;
        document.querySelectorAll("details[data-close-outside][open]").forEach(function (d) {
            d.open = false;
            var s = d.querySelector("summary");
            if (s) s.focus();
        });
        setDrawer(false);
    });

    // "All filters" drawer.
    function setDrawer(open) {
        var drawer = document.getElementById("filters");
        if (!drawer) return;
        drawer.classList.toggle("is-open", open);
        drawer.setAttribute("aria-hidden", String(!open));
        document.body.classList.toggle("filters-open", open);
        document.querySelectorAll(".pill-all").forEach(function (b) { b.setAttribute("aria-expanded", String(open)); });
    }
    function drawerOpen() {
        var drawer = document.getElementById("filters");
        return !!drawer && drawer.classList.contains("is-open");
    }
    document.addEventListener("click", function (e) {
        if (e.target.closest("[data-toggle-filters]")) {
            setDrawer(!drawerOpen());
        } else if (drawerOpen() && !e.target.closest("#filters")) {
            setDrawer(false);
        }
    });

    // "Show all" for long facet lists.
    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-facet-more]");
        if (!btn) return;
        var list = btn.parentElement.querySelector(".facet-list");
        var expanded = list.classList.toggle("is-expanded");
        btn.textContent = expanded ? "Show less" : btn.dataset.label;
    });

    // Quantity stepper.
    document.addEventListener("click", function (e) {
        var btn = e.target.closest("[data-qty]");
        if (!btn) return;
        var input = btn.parentElement.querySelector("input");
        var min = Number(input.min || 1), max = Number(input.max || 99);
        input.value = String(Math.min(max, Math.max(min, (Number(input.value) || 1) + Number(btn.dataset.qty))));
    });

    // Ask before destructive admin actions.
    document.addEventListener("submit", function (e) {
        var form = e.target;
        if (form.dataset && form.dataset.confirm && !window.confirm(form.dataset.confirm)) e.preventDefault();
    });

    // Checkout: update delivery fee and total when the delivery area changes.
    document.addEventListener("change", function (e) {
        var radio = e.target;
        if (radio.name !== "Input.DeliveryZone") return;
        document.querySelectorAll("[data-delivery-fee]").forEach(function (el) { el.textContent = radio.dataset.feeText; });
        document.querySelectorAll("[data-order-total]").forEach(function (el) { el.textContent = radio.dataset.totalText; });
    });

    // Display settings (layout density and theme), remembered in this browser only.
    var prefDefaults = { density: "comfortable", theme: "system" };
    function readPref(name) {
        try { return localStorage.getItem("pref-" + name) || prefDefaults[name]; } catch (e) { return prefDefaults[name]; }
    }
    function applyPref(name, value) {
        var attr = name === "theme" ? "data-theme" : "data-density";
        if (value === prefDefaults[name]) document.documentElement.removeAttribute(attr);
        else document.documentElement.setAttribute(attr, value);
        try {
            if (value === prefDefaults[name]) localStorage.removeItem("pref-" + name);
            else localStorage.setItem("pref-" + name, value);
        } catch (e) { /* not saved, still applied for this page */ }
        syncHeaderHeight();
    }
    document.querySelectorAll("input[data-pref]").forEach(function (input) {
        input.checked = readPref(input.dataset.pref) === input.value;
    });
    document.addEventListener("change", function (e) {
        var input = e.target;
        if (input.dataset && input.dataset.pref && input.checked) applyPref(input.dataset.pref, input.value);
    });

    // Live countdown for time-limited offers (re-queried each tick, so swapped-in content is covered too).
    function plural(n, word) { return n + " " + word + (n === 1 ? "" : "s"); }
    function renderCountdowns() {
        var now = Date.now();
        document.querySelectorAll("[data-countdown]").forEach(function (el) {
            var end = Date.parse(el.dataset.countdown);
            if (isNaN(end)) return;
            var ms = end - now;
            if (ms <= 0) { el.textContent = "offer ended"; return; }
            var mins = Math.floor(ms / 60000);
            var d = Math.floor(mins / 1440), h = Math.floor((mins % 1440) / 60), m = mins % 60;
            el.textContent = d > 0 ? plural(d, "day") + " " + h + "h left"
                : h > 0 ? h + "h " + m + "m left"
                : plural(Math.max(1, m), "min") + " left";
        });
    }
    renderCountdowns();
    setInterval(renderCountdowns, 30000);

    // ---------------------------------------------------------------------------------------------
    // Listing: apply filters, sorting and paging in place — no page reload, no scroll jump.
    // The server returns just the listing body when asked with the X-Partial header.
    // ---------------------------------------------------------------------------------------------
    var root = document.getElementById("listing-root");
    if (!root || !window.fetch || !window.history.pushState) return;

    var status = document.getElementById("listing-status");
    var pending = null;

    root.addEventListener("click", function (e) {
        var a = e.target.closest("a[data-ajax]");
        if (!a || e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        e.preventDefault();
        load(a.href, { push: true, toTop: a.dataset.ajax === "page", focusKey: a.dataset.fk });
    });

    root.addEventListener("submit", function (e) {
        var form = e.target.closest("form[data-ajax]");
        if (!form) return;
        e.preventDefault();
        var params = new URLSearchParams();
        new FormData(form).forEach(function (value, key) { if (String(value).trim() !== "") params.append(key, value); });
        params.delete("page");
        var qs = params.toString();
        load(location.pathname + (qs ? "?" + qs : ""), { push: true });
    });

    window.addEventListener("popstate", function () {
        load(location.href, { push: false });
    });

    function load(url, opts) {
        if (pending) pending.abort();
        var controller = new AbortController();
        pending = controller;

        // Remember what's open so the new content looks the same around the user's focus.
        var openPill = root.querySelector("details.pill-menu[open]");
        var openKey = openPill ? openPill.dataset.key : null;
        var popScroll = openPill ? openPill.querySelector(".pill-pop").scrollTop : 0;
        var keepDrawer = drawerOpen();
        var drawerScroll = keepDrawer ? root.querySelector(".drawer-body").scrollTop : 0;
        var expanded = Array.prototype.map.call(root.querySelectorAll(".facet-list.is-expanded a[data-fk]"), function (a) { return a.dataset.fk.split(":")[0]; });

        root.classList.add("is-loading");
        root.setAttribute("aria-busy", "true");

        fetch(url, { headers: { "X-Partial": "listing" }, signal: controller.signal, credentials: "same-origin" })
            .then(function (res) {
                if (!res.ok) throw new Error("HTTP " + res.status);
                return res.text();
            })
            .then(function (html) {
                if (opts.push) history.pushState({ listing: true }, "", url);

                // Hold the page height until the swap is done, so a shorter result list
                // can't pull the page up under the user's scroll position.
                var rect = root.getBoundingClientRect();
                root.style.minHeight = Math.max(rect.height, window.innerHeight - rect.top) + "px";

                var swap = function () {
                    root.innerHTML = html;
                    restore(openKey, popScroll, keepDrawer, drawerScroll, expanded, opts.focusKey);
                    renderCountdowns();
                    nameCards();
                };

                if (document.startViewTransition && !reduceMotion) {
                    nameCards();
                    var t = document.startViewTransition(swap);
                    t.finished.finally(done);
                } else {
                    swap();
                    done();
                }

                if (opts.toTop) {
                    var top = root.getBoundingClientRect().top + window.scrollY - (header ? header.offsetHeight : 0) - 12;
                    window.scrollTo({ top: top, behavior: reduceMotion ? "auto" : "smooth" });
                }

                var count = root.querySelector("[data-listing-count]");
                if (status && count) status.textContent = count.textContent;
            })
            .catch(function (err) {
                if (err.name === "AbortError") return;
                window.location.href = url; // fall back to a normal page load
            });

        function done() {
            if (pending !== controller) return;
            pending = null;
            root.classList.remove("is-loading");
            root.removeAttribute("aria-busy");
            root.style.minHeight = "";
        }
    }

    // Give each product card a stable name so the browser can animate it to its new position.
    function nameCards() {
        root.querySelectorAll(".results .card").forEach(function (card) {
            var link = card.querySelector(".card-title a");
            if (link) card.style.viewTransitionName = "p-" + link.getAttribute("href").replace(/[^a-z0-9-]/gi, "-");
        });
    }

    function restore(openKey, popScroll, keepDrawer, drawerScroll, expanded, focusKey) {
        if (openKey) {
            var pill = root.querySelector('details.pill-menu[data-key="' + CSS.escape(openKey) + '"]');
            if (pill) {
                pill.open = true;
                var pop = pill.querySelector(".pill-pop");
                if (pop) pop.scrollTop = popScroll;
            }
        }
        if (keepDrawer) {
            var drawer = document.getElementById("filters");
            drawer.style.transition = "none";
            setDrawer(true);
            root.querySelector(".drawer-body").scrollTop = drawerScroll;
            drawer.offsetHeight; // apply without animating
            drawer.style.transition = "";
        }
        expanded.forEach(function (key) {
            root.querySelectorAll('a[data-fk^="' + CSS.escape(key) + ':"]').forEach(function (a) {
                var list = a.closest(".facet-list");
                if (list) {
                    list.classList.add("is-expanded");
                    var btn = list.parentElement.querySelector("[data-facet-more]");
                    if (btn) btn.textContent = "Show less";
                }
            });
        });
        if (focusKey) {
            var scope = keepDrawer ? document.getElementById("filters") : (openKey ? root.querySelector("details.pill-menu[open]") : root);
            var target = scope && scope.querySelector('[data-fk="' + CSS.escape(focusKey) + '"]');
            if (target) target.focus({ preventScroll: true });
        }
    }
})();
