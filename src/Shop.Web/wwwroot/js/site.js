// Small progressive enhancements. Every page works without JavaScript.
(function () {
    "use strict";

    // Submit a form as soon as a select changes (sort order, cart quantity).
    document.querySelectorAll("[data-autosubmit]").forEach(function (el) {
        el.addEventListener("change", function () { el.form && el.form.submit(); });
    });

    // Mobile filter drawer.
    var filters = document.getElementById("filters");
    document.querySelectorAll("[data-toggle-filters]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            if (!filters) return;
            var open = !filters.classList.contains("is-open");
            filters.classList.toggle("is-open", open);
            document.body.classList.toggle("filters-open", open);
            document.querySelectorAll(".filter-toggle").forEach(function (b) { b.setAttribute("aria-expanded", String(open)); });
        });
    });
    document.addEventListener("click", function (e) {
        if (filters && document.body.classList.contains("filters-open") && !filters.contains(e.target) && !e.target.closest("[data-toggle-filters]")) {
            filters.classList.remove("is-open");
            document.body.classList.remove("filters-open");
        }
    });

    // "Show all" for long facet lists.
    document.querySelectorAll("[data-facet-more]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            var list = btn.parentElement.querySelector(".facet-list");
            var expanded = list.classList.toggle("is-expanded");
            btn.textContent = expanded ? "Show less" : btn.dataset.label || btn.textContent;
        });
        btn.dataset.label = btn.textContent;
    });

    // Quantity stepper.
    document.querySelectorAll("[data-qty]").forEach(function (btn) {
        btn.addEventListener("click", function () {
            var input = btn.parentElement.querySelector("input");
            var min = Number(input.min || 1), max = Number(input.max || 99);
            var next = Math.min(max, Math.max(min, (Number(input.value) || 1) + Number(btn.dataset.qty)));
            input.value = String(next);
        });
    });

    // Live countdown for time-limited offers.
    var timers = document.querySelectorAll("[data-countdown]");
    function plural(n, word) { return n + " " + word + (n === 1 ? "" : "s"); }
    function render() {
        var now = Date.now();
        timers.forEach(function (el) {
            var end = Date.parse(el.dataset.countdown);
            var ms = end - now;
            if (isNaN(end)) return;
            if (ms <= 0) { el.textContent = "0 min (ended)"; return; }
            var mins = Math.floor(ms / 60000);
            var d = Math.floor(mins / 1440), h = Math.floor((mins % 1440) / 60), m = mins % 60;
            el.textContent = d > 0 ? plural(d, "day") + " " + plural(h, "hour") + " left"
                : h > 0 ? plural(h, "hour") + " " + plural(m, "min") + " left"
                : plural(Math.max(1, m), "min") + " left";
        });
    }
    if (timers.length) { render(); setInterval(render, 30000); }
})();

// Ask before destructive admin actions.
document.querySelectorAll("form[data-confirm]").forEach(function (form) {
    form.addEventListener("submit", function (e) {
        if (!window.confirm(form.dataset.confirm)) e.preventDefault();
    });
});
