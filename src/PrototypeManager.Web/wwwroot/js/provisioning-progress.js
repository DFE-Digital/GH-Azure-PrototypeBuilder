(() => {
    const root = document.getElementById("provisioning-progress");
    if (!root) {
        return;
    }

    const progressUrl = root.dataset.progressUrl;
    const defaultAdminUrl = root.dataset.adminUrl;
    const tasksList = document.getElementById("provisioning-progress-tasks");
    const retryFormsHold = document.getElementById("provisioning-progress-retry-forms");
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
    const liveUrlButton = document.getElementById("provisioning-progress-live-url");
    const adminUrlButton = document.getElementById("provisioning-progress-admin-url");
    const failedUrlButton = document.getElementById("provisioning-progress-failed-url");
    const failedManageUrlButton = document.getElementById("provisioning-progress-failed-manage-url");
    const statusNotification = document.getElementById("setup-status-notification");
    const stageCurrentLabel = document.getElementById("setup-stage-current-label");
    const stageCurrentBody = document.getElementById("setup-stage-current-body");
    const stageList = document.getElementById("setup-stage-list");
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

    const stashRetryForms = () => {
        if (!retryFormsHold) {
            return;
        }

        document.querySelectorAll("[data-task-retry-slot] form.setup-page__retry-form").forEach((form) => {
            form.hidden = true;
            retryFormsHold.appendChild(form);
        });
    };

    const getRetryTemplate = (kind) => {
        if (!retryFormsHold) {
            return null;
        }

        return retryFormsHold.querySelector(`form[data-retry-kind="${kind}"]`);
    };

    const placeRetryForm = (slot, kind, label) => {
        const template = getRetryTemplate(kind);
        if (!slot || !template) {
            return;
        }

        const labelElement = template.querySelector("[data-retry-label]");
        if (labelElement && label) {
            labelElement.textContent = label;
        }

        template.hidden = false;
        slot.hidden = false;
        slot.appendChild(template);
    };

    const renderTasks = (tasks, retry) => {
        if (!tasksList) {
            return;
        }

        stashRetryForms();

        const failedTaskId = retry?.failedTaskId ?? null;
        const canRetryScaffold = Boolean(retry?.canRetryScaffold);
        const canRetryProvisioning = Boolean(retry?.canRetryProvisioning);
        const canRetryGitHubDeployment = Boolean(retry?.canRetryGitHubDeployment);
        const blockedReason = retry?.retryBlockedReason ?? "";
        const retryLabel = retry?.retryButtonLabel || "Retry this step";
        const canRetry = canRetryScaffold || canRetryProvisioning || canRetryGitHubDeployment;

        tasksList.innerHTML = tasks.map((task, index) => {
            const statusClass = statusClasses[task.status] ?? "setup-task-list__item--pending";
            const badgeClass = badgeClasses[task.status] ?? badgeClasses.Pending;
            const statusLabel = statusLabels[task.status] ?? statusLabels.Pending;
            const hint = task.description
                ? `<p class="setup-task-list__hint">${escapeHtml(task.description)}</p>`
                : "";
            const isFailedTask = task.status === "Failed";
            const isRetryTarget = isFailedTask && failedTaskId && task.id === failedTaskId;
            const blockedMarkup = isRetryTarget && !canRetry && blockedReason
                ? `<p class="setup-task-list__retry-blocked">${escapeHtml(blockedReason)}</p>`
                : "";

            return `<li class="setup-task-list__item ${statusClass}" data-task-id="${escapeHtml(task.id)}">
                <span class="setup-task-list__marker" aria-hidden="true">${index + 1}</span>
                <div class="setup-task-list__content">
                    <div class="setup-task-list__header">
                        <h3 class="setup-task-list__title">${escapeHtml(task.label)}</h3>
                        <span class="${badgeClass}">${statusLabel}</span>
                    </div>
                    ${hint}
                    <div class="setup-task-list__retry" data-task-retry-slot hidden></div>
                    ${blockedMarkup}
                </div>
            </li>`;
        }).join("");

        if (!canRetry || !failedTaskId) {
            return;
        }

        const failedItem = tasksList.querySelector(`[data-task-id="${CSS.escape(failedTaskId)}"]`);
        const slot = failedItem?.querySelector("[data-task-retry-slot]");
        if (!slot) {
            return;
        }

        if (canRetryScaffold) {
            placeRetryForm(slot, "scaffold", retryLabel);
        } else if (canRetryProvisioning) {
            placeRetryForm(slot, "provisioning", retryLabel);
        } else if (canRetryGitHubDeployment) {
            placeRetryForm(slot, "github", retryLabel);
        }
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

    const updateFailedSummary = (data) => {
        if (!failedSummary) {
            return;
        }

        const canRetry = Boolean(data.canRetryProvisioning || data.canRetryScaffold || data.canRetryGitHubDeployment);
        const blockedReason = data.retryBlockedReason ?? "";

        if (canRetry) {
            failedSummary.textContent = "Fix the issue if you can, then use Retry on the failed step above.";
        } else if (blockedReason) {
            failedSummary.textContent = blockedReason;
        } else {
            failedSummary.textContent = "Review the failed task above. Retry if you can, or delete this prototype and start again.";
        }
    };

    const updateStatusNotification = (data) => {
        if (!statusNotification) {
            return;
        }

        const showNotification = Boolean(data.isComplete || data.isFailed);
        statusNotification.hidden = !showNotification;
    };

    const resolveCurrentStage = (tasks, isComplete) => {
        if (isComplete) {
            return null;
        }

        return tasks.find((task) => task.status === "InProgress" || task.status === "Failed")
            ?? tasks.find((task) => task.status === "Pending")
            ?? null;
    };

    const updateGuidance = (data) => {
        if (!stageList) {
            return;
        }

        const tasks = data.tasks ?? [];
        const current = resolveCurrentStage(tasks, data.isComplete);
        const currentId = current?.id ?? null;

        stageList.querySelectorAll("[data-stage-id]").forEach((item) => {
            const isCurrent = currentId && item.dataset.stageId === currentId;
            item.classList.toggle("setup-page__guidance-stage--current", Boolean(isCurrent));
        });

        if (stageCurrentLabel) {
            stageCurrentLabel.textContent = data.isComplete
                ? "Setup complete"
                : (current
                    ? (document.querySelector(`[data-stage-id="${CSS.escape(current.id)}"]`)?.dataset.stageLabel
                        ?? current.label)
                    : "Getting ready");
        }

        if (stageCurrentBody) {
            if (data.isComplete) {
                stageCurrentBody.textContent =
                    "Hosting is ready. You can open the live prototype or manage settings from the details page.";
            } else if (currentId) {
                const stageItem = stageList.querySelector(`[data-stage-id="${CSS.escape(currentId)}"]`);
                stageCurrentBody.textContent = stageItem?.dataset.stageExplanation
                    ?? "Prototype Manager is preparing the next setup step.";
            } else {
                stageCurrentBody.textContent = "Prototype Manager is preparing the next setup step.";
            }
        }
    };

    const updatePage = (data) => {
        if (data.tasks?.length) {
            renderTasks(data.tasks, data);
            updateProgressMetrics(data.tasks);
            updateGuidance(data);
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
            updateFailedSummary(data);
        } else {
            stashRetryForms();
        }

        const adminUrl = data.adminUrl || defaultAdminUrl;
        if (adminUrlButton && adminUrl) {
            adminUrlButton.href = adminUrl;
        }

        if (failedUrlButton && adminUrl) {
            failedUrlButton.href = adminUrl;
        }

        if (failedManageUrlButton && adminUrl) {
            failedManageUrlButton.href = adminUrl;
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

    const placeInitialRetry = () => {
        if (!retryFormsHold) {
            return;
        }

        const failedTaskId = retryFormsHold.dataset.initialFailedTaskId;
        const canRetryScaffold = retryFormsHold.dataset.initialCanRetryScaffold === "true";
        const canRetryProvisioning = retryFormsHold.dataset.initialCanRetryProvisioning === "true";
        const canRetryGitHub = retryFormsHold.dataset.initialCanRetryGithub === "true";
        const retryLabel = retryFormsHold.dataset.initialRetryLabel || "Retry this step";

        if (!failedTaskId || !(canRetryScaffold || canRetryProvisioning || canRetryGitHub)) {
            return;
        }

        const failedItem = tasksList?.querySelector(`[data-task-id="${CSS.escape(failedTaskId)}"]`);
        const slot = failedItem?.querySelector("[data-task-retry-slot]");
        if (!slot) {
            return;
        }

        if (canRetryScaffold) {
            placeRetryForm(slot, "scaffold", retryLabel);
        } else if (canRetryProvisioning) {
            placeRetryForm(slot, "provisioning", retryLabel);
        } else if (canRetryGitHub) {
            placeRetryForm(slot, "github", retryLabel);
        }
    };

    placeInitialRetry();

    const pollTimer = window.setInterval(refresh, pollIntervalMs);
    refresh();
})();
