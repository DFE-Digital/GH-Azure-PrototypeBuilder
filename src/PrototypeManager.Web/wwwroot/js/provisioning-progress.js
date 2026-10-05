(() => {
    const root = document.getElementById("provisioning-progress");
    if (!root) {
        return;
    }

    const progressUrl = root.dataset.progressUrl;
    const defaultAdminUrl = root.dataset.adminUrl;
    const tasksList = document.getElementById("provisioning-progress-tasks");
    const summary = document.getElementById("provisioning-progress-summary");
    const statusBadge = document.getElementById("provisioning-progress-status-badge");
    const progressPanel = document.getElementById("provisioning-progress-panel");
    const progressBar = document.getElementById("provisioning-progress-bar");
    const progressCount = document.getElementById("provisioning-progress-count");
    const progressPercent = document.getElementById("provisioning-progress-percent");
    const liveIndicator = document.getElementById("provisioning-progress-live");
    const completePanel = document.getElementById("provisioning-progress-complete");
    const failedPanel = document.getElementById("provisioning-progress-failed");
    const failedSummary = document.getElementById("provisioning-progress-failed-summary");
    const failedErrorElement = document.getElementById("provisioning-progress-failed-error");
    const retryActions = document.getElementById("provisioning-progress-retry-actions");
    const retryScaffoldForm = document.getElementById("provisioning-progress-retry-scaffold");
    const retryProvisioningForm = document.getElementById("provisioning-progress-retry-provisioning");
    const retryGitHubForm = document.getElementById("provisioning-progress-retry-github");
    const liveUrlButton = document.getElementById("provisioning-progress-live-url");
    const adminUrlButton = document.getElementById("provisioning-progress-admin-url");
    const failedUrlButton = document.getElementById("provisioning-progress-failed-url");
    const statusNotification = document.getElementById("setup-status-notification");
    const pollIntervalMs = 3000;

    const statusClasses = {
        Pending: "setup-task-list__item--pending",
        InProgress: "setup-task-list__item--inprogress",
        Completed: "setup-task-list__item--completed",
        Failed: "setup-task-list__item--failed",
        Skipped: "setup-task-list__item--skipped"
    };

    const badgeClasses = {
        Completed: "dfe-c-fe-badge dfe-c-fe-badge--success",
        InProgress: "dfe-c-fe-badge dfe-c-fe-badge--info",
        Failed: "dfe-c-fe-badge dfe-c-fe-badge--danger",
        Skipped: "dfe-c-fe-badge dfe-c-fe-badge--muted",
        Pending: "dfe-c-fe-badge dfe-c-fe-badge--muted"
    };

    const setupStatusClasses = {
        failed: "dfe-c-fe-badge dfe-c-fe-badge--danger",
        complete: "dfe-c-fe-badge dfe-c-fe-badge--success",
        active: "dfe-c-fe-badge dfe-c-fe-badge--info",
        waiting: "dfe-c-fe-badge dfe-c-fe-badge--muted"
    };

    const progressPanelClasses = {
        failed: "setup-page__progress-panel--failed",
        complete: "setup-page__progress-panel--complete",
        active: "setup-page__progress-panel--active"
    };

    const statusLabels = {
        Completed: "Completed",
        InProgress: "In progress",
        Failed: "Failed",
        Skipped: "Skipped",
        Pending: "Not started"
    };

    const escapeHtml = (value) => String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll("\"", "&quot;");

    const renderTaskError = (task, errorMessage) => {
        if (task.status !== "Failed" || !errorMessage) {
            return "";
        }

        return `<details class="setup-task-list__error-details">
            <summary class="setup-task-list__error-summary">View error details</summary>
            <pre class="setup-task-list__error-message">${escapeHtml(errorMessage)}</pre>
        </details>`;
    };

    const renderTasks = (tasks, errorMessage) => {
        if (!tasksList) {
            return;
        }

        tasksList.innerHTML = tasks.map((task, index) => {
            const statusClass = statusClasses[task.status] ?? "setup-task-list__item--pending";
            const badgeClass = badgeClasses[task.status] ?? badgeClasses.Pending;
            const statusLabel = statusLabels[task.status] ?? statusLabels.Pending;
            const hint = task.description
                ? `<p class="setup-task-list__hint">${escapeHtml(task.description)}</p>`
                : "";

            return `<li class="setup-task-list__item ${statusClass}" data-task-id="${escapeHtml(task.id)}">
                <span class="setup-task-list__marker" aria-hidden="true">${index + 1}</span>
                <div class="setup-task-list__content">
                    <div class="setup-task-list__header">
                        <h3 class="setup-task-list__title">${escapeHtml(task.label)}</h3>
                        <span class="${badgeClass}">${statusLabel}</span>
                    </div>
                    ${hint}
                    ${renderTaskError(task, errorMessage)}
                </div>
            </li>`;
        }).join("");
    };

    const updateProgressMetrics = (tasks) => {
        const total = tasks?.length ?? 0;
        const completed = tasks?.filter((task) => task.status === "Completed").length ?? 0;
        const percent = total === 0 ? 0 : Math.round((completed / total) * 100);

        if (progressBar) {
            progressBar.style.width = `${percent}%`;
        }

        if (progressCount) {
            progressCount.textContent = `${completed} of ${total} tasks complete`;
        }

        if (progressPercent) {
            progressPercent.textContent = `${percent}%`;
        }
    };

    const updateProgressPanelState = (data) => {
        if (!progressPanel) {
            return;
        }

        progressPanel.classList.remove(
            progressPanelClasses.failed,
            progressPanelClasses.complete,
            progressPanelClasses.active
        );

        if (data.isFailed) {
            progressPanel.classList.add(progressPanelClasses.failed);
        } else if (data.isComplete) {
            progressPanel.classList.add(progressPanelClasses.complete);
        } else if (data.isActive) {
            progressPanel.classList.add(progressPanelClasses.active);
        }
    };

    const updateSetupStatusBadge = (data) => {
        if (!statusBadge) {
            return;
        }

        if (data.isFailed) {
            statusBadge.className = setupStatusClasses.failed;
            statusBadge.textContent = "Setup failed";
            return;
        }

        if (data.isComplete) {
            statusBadge.className = setupStatusClasses.complete;
            statusBadge.textContent = "Setup complete";
            return;
        }

        if (data.isActive) {
            statusBadge.className = setupStatusClasses.active;
            statusBadge.textContent = "In progress";
            return;
        }

        statusBadge.className = setupStatusClasses.waiting;
        statusBadge.textContent = "Waiting to start";
    };

    const updateFailedPanelError = (errorMessage) => {
        if (!failedPanel) {
            return;
        }

        const existingDetails = failedPanel.querySelector(".setup-page__error-details");

        if (!errorMessage) {
            existingDetails?.remove();
            return;
        }

        if (existingDetails) {
            const pre = existingDetails.querySelector(".setup-page__error-message");
            if (pre) {
                pre.textContent = errorMessage;
            }
            return;
        }

        const content = failedPanel.querySelector(".setup-page__outcome-content");
        if (!content) {
            return;
        }

        const details = document.createElement("details");
        details.className = "setup-page__error-details";
        details.innerHTML = `<summary class="setup-page__error-summary">Technical error details</summary>
            <pre class="setup-page__error-message" id="provisioning-progress-failed-error">${escapeHtml(errorMessage)}</pre>`;
        content.appendChild(details);
    };

    const updateRetryActions = (data) => {
        const canRetryProvisioning = Boolean(data.canRetryProvisioning);
        const canRetryScaffold = Boolean(data.canRetryScaffold);
        const canRetryGitHubDeployment = Boolean(data.canRetryGitHubDeployment);
        const blockedReason = data.retryBlockedReason ?? "";

        if (failedSummary) {
            if (canRetryProvisioning || canRetryScaffold || canRetryGitHubDeployment) {
                failedSummary.textContent = "Fix the issue if you can, then retry the failed step below.";
            } else if (blockedReason) {
                failedSummary.textContent = blockedReason;
            } else {
                failedSummary.textContent = "Review the failed task above. Retry if you can, or delete this prototype and start again.";
            }
        }

        if (retryScaffoldForm) {
            retryScaffoldForm.hidden = !canRetryScaffold;
        }

        if (retryProvisioningForm) {
            retryProvisioningForm.hidden = !canRetryProvisioning;
        }

        if (retryGitHubForm) {
            retryGitHubForm.hidden = !canRetryGitHubDeployment;
        }
    };

    const updateStatusNotification = (data) => {
        if (!statusNotification) {
            return;
        }

        const showNotification = Boolean(data.isComplete || data.isFailed);
        statusNotification.hidden = !showNotification;
    };

    const updatePage = (data) => {
        if (data.tasks?.length) {
            renderTasks(data.tasks, data.errorMessage);
            updateProgressMetrics(data.tasks);
        }

        if (summary) {
            if (data.isFailed) {
                summary.textContent = "Setup failed. Review the failed task and error details below.";
            } else if (data.isComplete) {
                summary.textContent = "All setup tasks are complete. Your prototype is ready to use.";
            } else if (data.isActive) {
                summary.textContent = "This page updates automatically while your prototype is being created and deployed.";
            } else {
                summary.textContent = "Waiting for the next setup task to start.";
            }
        }

        updateSetupStatusBadge(data);
        updateProgressPanelState(data);
        updateStatusNotification(data);

        if (liveIndicator) {
            liveIndicator.hidden = !data.isActive;
        }

        if (completePanel) {
            completePanel.hidden = !(data.isComplete && !data.isFailed);
        }

        if (failedPanel) {
            failedPanel.hidden = !data.isFailed;
        }

        if (data.isFailed) {
            updateFailedPanelError(data.errorMessage);
            updateRetryActions(data);
        } else {
            if (retryProvisioningForm) {
                retryProvisioningForm.hidden = true;
            }

            if (retryGitHubForm) {
                retryGitHubForm.hidden = true;
            }
        }

        const adminUrl = data.adminUrl || defaultAdminUrl;
        if (adminUrlButton && adminUrl) {
            adminUrlButton.href = adminUrl;
        }

        if (failedUrlButton && adminUrl) {
            failedUrlButton.href = adminUrl;
        }

        if (liveUrlButton && data.prototypeUrl) {
            liveUrlButton.href = data.prototypeUrl;
        }

        if (data.isComplete) {
            window.clearInterval(pollTimer);
        }
    };

    const refresh = async () => {
        try {
            const response = await fetch(progressUrl, {
                headers: { Accept: "application/json" }
            });

            if (!response.ok) {
                return;
            }

            const data = await response.json();
            updatePage(data);
        } catch {
            // Keep polling; transient network errors are expected during long jobs.
        }
    };

    const pollTimer = window.setInterval(refresh, pollIntervalMs);
    refresh();
})();
