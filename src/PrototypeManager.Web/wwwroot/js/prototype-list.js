(() => {
    const searchInput = document.getElementById("prototype-list-search");
    const statusFilters = Array.from(document.querySelectorAll("[data-status-filter]"));
    const countElement = document.getElementById("prototype-list-count");
    const emptyResults = document.getElementById("prototype-list-empty-results");
    const clearFiltersButton = document.getElementById("prototype-list-clear-filters");
    const appList = document.getElementById("prototype-app-list");
    if (!searchInput || statusFilters.length === 0 || !appList) {
        return;
    }

    const snapshotUrl = appList.dataset.snapshotUrl || "";
    const pollIntervalMs = Number(appList.dataset.pollIntervalMs || "3000");
    let activeFilter = "all";
    let pollTimer = null;
    let lastSignature = "";

    const normalise = (value) => value.trim().toLowerCase();

    const escapeHtml = (value) => String(value ?? "")
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

    const getRows = () => Array.from(appList.querySelectorAll(".dfe-c-fe-app-row"));

    const getFilterValue = (button) =>
        (button.getAttribute("data-status-filter") || "all").toLowerCase();

    const setActiveFilter = (filter) => {
        activeFilter = filter;

        statusFilters.forEach((button) => {
            const isActive = getFilterValue(button) === filter;
            button.classList.toggle("dfe-c-fe-filter-chip--active", isActive);
            button.setAttribute("aria-pressed", isActive ? "true" : "false");
        });
    };

    const matchesQuery = (searchText, query) => {
        if (!query) {
            return true;
        }

        const tokens = query.split(/\s+/).filter(Boolean);
        return tokens.every((token) => searchText.includes(token));
    };

    const updateView = () => {
        const query = normalise(searchInput.value);
        let visibleCount = 0;

        getRows().forEach((row) => {
            const status = (row.getAttribute("data-status") || "").toLowerCase();
            const searchText = (row.getAttribute("data-search") || "").toLowerCase();
            const matchesFilter = activeFilter === "all" || status === activeFilter;
            const matchesSearch = matchesQuery(searchText, query);
            const visible = matchesFilter && matchesSearch;

            row.hidden = !visible;
            row.classList.toggle("is-filtered-out", !visible);

            if (visible) {
                visibleCount += 1;
            }
        });

        if (countElement) {
            const label = visibleCount === 1 ? "prototype" : "prototypes";
            countElement.textContent = `Showing ${visibleCount} ${label}`;
        }

        if (emptyResults) {
            emptyResults.hidden = visibleCount > 0;
        }

        appList.hidden = visibleCount === 0 && getRows().length > 0;
    };

    const setCount = (selector, value) => {
        const element = document.querySelector(selector);
        if (element) {
            element.textContent = String(value);
        }
    };

    const updateCounts = (data) => {
        const total = data.prototypes?.length ?? 0;

        setCount('[data-filter-count="all"]', total);
        setCount('[data-filter-count="live"]', data.liveCount ?? 0);
        setCount('[data-filter-count="active"]', data.activeJobCount ?? 0);
        setCount('[data-filter-count="failed"]', data.failedCount ?? 0);
        setCount('[data-filter-count="archived"]', data.archivedCount ?? 0);

        document.querySelectorAll(".app-side-nav__item .app-side-nav__count").forEach((countEl) => {
            const link = countEl.closest("a");
            if (!link) {
                return;
            }

            const href = link.getAttribute("href") || "";
            if (href.includes("scope=mine") || (!href.includes("scope=") && !href.includes("team="))) {
                // Keep "Your prototypes" in sync when possible.
                if (typeof data.yourPrototypeCount === "number" && href.includes("scope=mine")) {
                    countEl.textContent = String(data.yourPrototypeCount);
                }
            }

            if (href.includes("scope=all") && typeof data.allPrototypeCount === "number") {
                countEl.textContent = String(data.allPrototypeCount);
            }

            if (href.includes("team=") && Array.isArray(data.teams)) {
                const teamId = new URL(href, window.location.origin).searchParams.get("team");
                const team = data.teams.find((item) => String(item.id).toLowerCase() === String(teamId).toLowerCase());
                if (team) {
                    countEl.textContent = String(team.count);
                }
            }
        });

        const yourLink = document.querySelector('.app-side-nav__link[href*="scope=mine"] .app-side-nav__count');
        if (yourLink && typeof data.yourPrototypeCount === "number") {
            yourLink.textContent = String(data.yourPrototypeCount);
        }
    };

    const renderOpenControl = (prototype) => {
        if (prototype.hasOpenUrl && prototype.openUrl) {
            return `<a class="govuk-button dfe-c-fe-app-row__action"
                       href="${escapeHtml(prototype.openUrl)}">
                        Open<span class="govuk-visually-hidden"> ${escapeHtml(prototype.name)}</span>
                    </a>`;
        }

        return `<button type="button"
                        class="govuk-button dfe-c-fe-app-row__action"
                        disabled
                        aria-disabled="true">
                    Open<span class="govuk-visually-hidden"> ${escapeHtml(prototype.name)} (not available yet)</span>
                </button>`;
    };

    const renderRepositoryMeta = (prototype) => {
        if (!prototype.repositoryName || !prototype.repositoryUrl) {
            return "";
        }

        return `<span class="dfe-c-fe-app-row__meta-sep" aria-hidden="true"> · </span>` +
            `<a class="govuk-link" href="${escapeHtml(prototype.repositoryUrl)}" target="_blank" rel="noopener noreferrer">${escapeHtml(prototype.repositoryName)}</a>`;
    };

    const renderRow = (prototype) => {
        const initial = (prototype.name || "?").trim().charAt(0).toUpperCase() || "?";
        return `<li class="dfe-c-fe-app-row dfe-c-fe-app-row--list"
                    data-prototype-id="${escapeHtml(prototype.id)}"
                    data-status="${escapeHtml(prototype.statusFilter)}"
                    data-search="${escapeHtml(prototype.searchText)}">
            <div class="dfe-c-fe-app-row__name-col">
                <span class="dfe-c-fe-app-row__icon" aria-hidden="true">${escapeHtml(initial)}</span>
                <span class="dfe-c-fe-app-row__content">
                    <span class="dfe-c-fe-app-row__name">${escapeHtml(prototype.name)}</span>
                    <span class="dfe-c-fe-app-row__meta">
                        ${escapeHtml(prototype.businessArea)}${renderRepositoryMeta(prototype)}
                    </span>
                </span>
            </div>
            <div class="dfe-c-fe-app-row__status">
                <span class="${escapeHtml(prototype.badgeClass)}">${escapeHtml(prototype.badgeLabel)}</span>
            </div>
            <div class="dfe-c-fe-app-row__manage">
                <a class="govuk-button govuk-button--secondary dfe-c-fe-app-row__action"
                   href="${escapeHtml(prototype.manageUrl)}">
                    Manage<span class="govuk-visually-hidden"> ${escapeHtml(prototype.name)}</span>
                </a>
            </div>
            <div class="dfe-c-fe-app-row__open-col">
                ${renderOpenControl(prototype)}
            </div>
        </li>`;
    };

    const buildSignature = (data) =>
        JSON.stringify((data.prototypes || []).map((item) => [item.id, item.status, item.hasOpenUrl]));

    const stopPolling = () => {
        if (pollTimer) {
            window.clearInterval(pollTimer);
            pollTimer = null;
        }
    };

    const startPolling = () => {
        if (pollTimer || !snapshotUrl) {
            return;
        }

        pollTimer = window.setInterval(refreshSnapshot, pollIntervalMs);
    };

    const refreshSnapshot = async () => {
        if (!snapshotUrl) {
            return;
        }

        try {
            const response = await fetch(snapshotUrl, {
                headers: { Accept: "application/json" }
            });

            if (!response.ok) {
                return;
            }

            const data = await response.json();
            const signature = buildSignature(data);

            if (signature !== lastSignature) {
                const hadRows = getRows().length > 0;
                const nextCount = data.prototypes?.length ?? 0;

                if (hadRows && nextCount === 0) {
                    window.location.reload();
                    return;
                }

                lastSignature = signature;
                appList.innerHTML = (data.prototypes || []).map(renderRow).join("");
                updateCounts(data);
                updateView();
            } else {
                updateCounts(data);
            }

            if (data.hasActiveJobs) {
                startPolling();
            } else {
                stopPolling();
            }
        } catch {
            // Keep polling through transient errors while jobs are active.
        }
    };

    searchInput.addEventListener("input", updateView);
    searchInput.addEventListener("search", updateView);

    statusFilters.forEach((button) => {
        button.addEventListener("click", (event) => {
            event.preventDefault();
            setActiveFilter(getFilterValue(button));
            updateView();
        });
    });

    clearFiltersButton?.addEventListener("click", () => {
        searchInput.value = "";
        setActiveFilter("all");
        updateView();
        searchInput.focus();
    });

    updateView();

    lastSignature = buildSignature({
        prototypes: getRows().map((row) => ({
            id: row.dataset.prototypeId,
            status: row.dataset.status,
            hasOpenUrl: Boolean(row.querySelector('a.dfe-c-fe-app-row__action:not(.govuk-button--secondary)'))
        }))
    });

    if (appList.dataset.hasActiveJobs === "true") {
        startPolling();
        refreshSnapshot();
    }
})();
