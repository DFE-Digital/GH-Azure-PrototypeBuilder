(() => {
    const searchInput = document.getElementById("prototype-list-search");
    const statusFilters = Array.from(document.querySelectorAll(".dfe-c-fe-filter-chip"));
    const rows = Array.from(document.querySelectorAll(".dfe-c-fe-app-row[data-status]"));
    const countElement = document.getElementById("prototype-list-count");
    const emptyResults = document.getElementById("prototype-list-empty-results");
    const clearFiltersButton = document.getElementById("prototype-list-clear-filters");
    const appList = document.getElementById("prototype-app-list");

    if (!searchInput || statusFilters.length === 0 || rows.length === 0) {
        return;
    }

    let activeFilter = "all";

    const normalise = (value) => value.trim().toLowerCase();

    const setActiveFilter = (filter) => {
        activeFilter = filter;

        statusFilters.forEach(button => {
            const isActive = button.dataset.statusFilter === filter;
            button.classList.toggle("dfe-c-fe-filter-chip--active", isActive);
            button.setAttribute("aria-pressed", isActive ? "true" : "false");
        });
    };

    const updateView = () => {
        const query = normalise(searchInput.value);
        let visibleCount = 0;

        rows.forEach(row => {
            const status = row.dataset.status ?? "";
            const searchText = row.dataset.search ?? "";
            const matchesFilter = activeFilter === "all" || status === activeFilter;
            const matchesSearch = !query || searchText.includes(query);
            const visible = matchesFilter && matchesSearch;

            row.hidden = !visible;

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

        if (appList) {
            appList.hidden = visibleCount === 0;
        }
    };

    searchInput.addEventListener("input", updateView);

    statusFilters.forEach(button => {
        button.addEventListener("click", () => {
            setActiveFilter(button.dataset.statusFilter ?? "all");
            updateView();
        });
    });

    clearFiltersButton?.addEventListener("click", () => {
        searchInput.value = "";
        setActiveFilter("all");
        updateView();
        searchInput.focus();
    });
})();
