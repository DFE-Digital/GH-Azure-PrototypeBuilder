(() => {
    const root = document.getElementById("live-operations");
    if (!root) {
        return;
    }

    const feedUrl = root.dataset.feedUrl;
    const updatedAtElement = document.getElementById("live-operations-updated-at");
    const liveIndicator = document.getElementById("live-operations-indicator");
    const summaryRoot = document.getElementById("live-operations-summary");
    const activeRoot = document.getElementById("live-operations-active");
    const eventsRoot = document.getElementById("live-operations-events");
    const prototypesTable = document.getElementById("live-operations-prototypes");
    const pollIntervalMs = 3000;

    const escapeHtml = (value) => String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\"", "&quot;");

    const formatLocalTime = (isoValue) => {
        const date = new Date(isoValue);
        if (Number.isNaN(date.getTime())) {
            return isoValue;
        }

        return date.toLocaleTimeString(undefined, {
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });
    };

    const formatEventTime = (isoValue) => {
        const date = new Date(isoValue);
        if (Number.isNaN(date.getTime())) {
            return isoValue;
        }

        return date.toLocaleString(undefined, {
            day: "2-digit",
            month: "short",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit",
            second: "2-digit"
        });
    };

    const getEventClass = (status) => {
        switch (status) {
            case "Running":
                return "activity-feed__item--running";
            case "Queued":
                return "activity-feed__item--queued";
            case "Succeeded":
                return "activity-feed__item--succeeded";
            case "Failed":
                return "activity-feed__item--failed";
            default:
                return "activity-feed__item--recorded";
        }
    };

    const updateSummary = (summary) => {
        if (!summaryRoot || !summary) {
            return;
        }

        const total = summaryRoot.querySelector("[data-summary='total']");
        const live = summaryRoot.querySelector("[data-summary='live']");
        const inProgress = summaryRoot.querySelector("[data-summary='inProgress']");
        const failed = summaryRoot.querySelector("[data-summary='failed']");
        const activeJobs = summaryRoot.querySelector("[data-summary='activeJobs']");

        if (total) {
            total.textContent = String(summary.total ?? 0);
        }

        if (live) {
            live.textContent = `${summary.live ?? 0} live`;
        }

        if (inProgress) {
            inProgress.textContent = `${summary.inProgress ?? 0} in progress`;
        }

        if (failed) {
            failed.textContent = `${summary.failed ?? 0} failed`;
        }

        if (activeJobs) {
            activeJobs.textContent = `${summary.activeJobs ?? 0} active jobs`;
        }
    };

    const renderActiveOperations = (operations) => {
        if (!activeRoot) {
            return;
        }

        if (!operations?.length) {
            activeRoot.innerHTML = `<p class="dfe-c-fe-text govuk-!-margin-bottom-0 live-page__empty" data-empty="active">
                Nothing is running right now. This page will update automatically when a job starts.
            </p>`;
            return;
        }

        activeRoot.innerHTML = `<ul class="live-page__active-list govuk-!-margin-bottom-0">
            ${operations.map((operation) => {
                const businessArea = operation.businessAreaName
                    ? `<span class="live-page__active-meta-separator" aria-hidden="true">·</span>
                       <span>${escapeHtml(operation.businessAreaName)}</span>`
                    : "";
                const currentStep = operation.currentStepLabel
                    ? `<p class="live-page__active-step">Current step: ${escapeHtml(operation.currentStepLabel)}</p>`
                    : "";
                const setupLink = operation.setupUrl
                    ? `<p class="govuk-!-margin-bottom-0">
                           <a class="govuk-link" href="${escapeHtml(operation.setupUrl)}">View setup progress</a>
                       </p>`
                    : "";

                return `<li class="live-page__active-item" data-prototype-id="${escapeHtml(operation.prototypeId)}">
                    <div class="live-page__active-header">
                        <h3 class="live-page__active-title">
                            <a class="govuk-link" href="${escapeHtml(operation.detailUrl)}">${escapeHtml(operation.prototypeName)}</a>
                        </h3>
                        <span class="${escapeHtml(operation.statusBadgeClass)}">${escapeHtml(operation.statusLabel)}</span>
                    </div>
                    <p class="live-page__active-meta">
                        <span class="${escapeHtml(operation.jobStatusBadgeClass)}">${escapeHtml(operation.jobStatusLabel)}</span>
                        <span class="live-page__active-meta-separator" aria-hidden="true">·</span>
                        <span>${escapeHtml(operation.jobTypeLabel)}</span>
                        ${businessArea}
                    </p>
                    ${currentStep}
                    ${setupLink}
                </li>`;
            }).join("")}
        </ul>`;
    };

    const renderRecentEvents = (events) => {
        if (!eventsRoot) {
            return;
        }

        if (!events?.length) {
            eventsRoot.innerHTML = `<p class="dfe-c-fe-text govuk-!-margin-bottom-0 live-page__empty" data-empty="events">
                No recent activity yet.
            </p>`;
            return;
        }

        eventsRoot.innerHTML = `<ol class="activity-feed govuk-!-margin-bottom-0">
            ${events.map((item) => {
                const summary = item.summary
                    ? `<p class="activity-feed__summary">${escapeHtml(item.summary)}</p>`
                    : "";

                return `<li class="activity-feed__item ${getEventClass(item.status)}" data-event-id="${escapeHtml(item.id)}">
                    <div class="activity-feed__marker" aria-hidden="true"></div>
                    <article class="activity-feed__content">
                        <div class="activity-feed__header">
                            <h3 class="activity-feed__title">
                                <a class="govuk-link" href="${escapeHtml(item.detailUrl)}">${escapeHtml(item.prototypeName)}</a>
                                <span class="live-page__event-action">${escapeHtml(item.action)}</span>
                            </h3>
                            <span class="${escapeHtml(item.statusBadgeClass)}">${escapeHtml(item.statusLabel)}</span>
                        </div>
                        <p class="activity-feed__meta">
                            <time datetime="${escapeHtml(item.occurredAt)}">${escapeHtml(formatEventTime(item.occurredAt))}</time>
                        </p>
                        ${summary}
                    </article>
                </li>`;
            }).join("")}
        </ol>`;
    };

    const renderPrototypeRows = (prototypes) => {
        if (!prototypesTable || !prototypes?.length) {
            return;
        }

        const tbody = prototypesTable.querySelector("tbody");
        if (!tbody) {
            return;
        }

        tbody.innerHTML = prototypes.map((prototype) => {
            const rowClass = prototype.hasActiveJob ? "live-page__table-row--active" : "";
            const businessArea = prototype.businessAreaName ?? "—";
            const activeJob = prototype.activeJobLabel ?? "—";

            return `<tr class="govuk-table__row ${rowClass}" data-prototype-id="${escapeHtml(prototype.prototypeId)}">
                <td class="govuk-table__cell">
                    <a class="govuk-link govuk-!-font-weight-bold" href="${escapeHtml(prototype.detailUrl)}">${escapeHtml(prototype.prototypeName)}</a>
                </td>
                <td class="govuk-table__cell">${escapeHtml(businessArea)}</td>
                <td class="govuk-table__cell">${escapeHtml(prototype.ownerEmail)}</td>
                <td class="govuk-table__cell">
                    <span class="${escapeHtml(prototype.statusBadgeClass)}">${escapeHtml(prototype.statusLabel)}</span>
                </td>
                <td class="govuk-table__cell">${escapeHtml(activeJob)}</td>
            </tr>`;
        }).join("");
    };

    const updatePage = (data) => {
        if (updatedAtElement && data.updatedAt) {
            updatedAtElement.dateTime = data.updatedAt;
            updatedAtElement.textContent = formatLocalTime(data.updatedAt);
        }

        updateSummary(data.summary);
        renderActiveOperations(data.activeOperations);
        renderRecentEvents(data.recentEvents);
        renderPrototypeRows(data.prototypes);
    };

    const refresh = async () => {
        if (liveIndicator) {
            liveIndicator.hidden = false;
        }

        try {
            const response = await fetch(feedUrl, {
                headers: { Accept: "application/json" }
            });

            if (!response.ok) {
                return;
            }

            const data = await response.json();
            updatePage(data);
        } catch {
            // Keep polling; transient network errors are expected.
        } finally {
            if (liveIndicator) {
                window.setTimeout(() => {
                    liveIndicator.hidden = true;
                }, 400);
            }
        }
    };

    window.setInterval(refresh, pollIntervalMs);
    refresh();
})();
