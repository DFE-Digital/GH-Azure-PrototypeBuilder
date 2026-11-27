(() => {
    const openDialog = (id) => {
        const dialog = document.getElementById(id);

        if (dialog instanceof HTMLDialogElement && typeof dialog.showModal === "function") {
            dialog.showModal();
        }
    };

    const manageContactModal = document.getElementById("manage-contact-modal");
    const manageContactTitle = document.getElementById("manage-contact-modal-title");
    const manageContactEmail = document.getElementById("manage-contact-modal-email");
    const ownerFields = document.getElementById("manage-contact-owner-fields");
    const makeOwnerButton = document.getElementById("manage-contact-make-owner-button");
    const removeButton = document.getElementById("manage-contact-remove-button");
    const removeDeputyButton = document.getElementById("manage-contact-remove-deputy-button");
    const makeMemberButton = document.getElementById("manage-contact-make-member-button");
    const transferEmailInput = document.getElementById("manage-contact-transfer-email");
    const removeContactIdInput = document.getElementById("manage-contact-remove-id");
    const ownerSelect = document.getElementById("manage-contact-new-owner-select");

    const setButtonVisible = (button, visible) => {
        if (!button) {
            return;
        }

        button.hidden = !visible;
        button.disabled = !visible;
    };

    document.querySelectorAll(".dfe-f-manage-contact-open").forEach(trigger => {
        trigger.addEventListener("click", (event) => {
            event.preventDefault();

            if (!manageContactModal) {
                return;
            }

            const contactType = trigger.dataset.contactType ?? "member";
            const contactName = trigger.dataset.contactName ?? "contact";
            const contactEmail = trigger.dataset.contactEmail ?? "";
            const contactId = trigger.dataset.contactId ?? "";

            if (manageContactTitle) {
                manageContactTitle.textContent = `Manage ${contactName}`;
            }

            if (manageContactEmail) {
                manageContactEmail.textContent = contactEmail;
            }

            setButtonVisible(makeOwnerButton, false);
            setButtonVisible(removeButton, false);
            setButtonVisible(removeDeputyButton, false);
            setButtonVisible(makeMemberButton, false);
            ownerFields?.setAttribute("hidden", "hidden");

            if (ownerSelect) {
                ownerSelect.required = false;
                ownerSelect.value = "";
            }

            if (contactType === "owner") {
                ownerFields?.removeAttribute("hidden");

                if (ownerSelect) {
                    ownerSelect.required = true;
                }

                setButtonVisible(makeMemberButton, true);
            } else if (contactType === "deputy") {
                if (transferEmailInput) {
                    transferEmailInput.value = contactEmail;
                }

                setButtonVisible(makeOwnerButton, true);
                setButtonVisible(removeDeputyButton, true);
            } else {
                if (transferEmailInput) {
                    transferEmailInput.value = contactEmail;
                }

                if (removeContactIdInput) {
                    removeContactIdInput.value = contactId;
                }

                setButtonVisible(makeOwnerButton, true);
                setButtonVisible(removeButton, true);
            }

            openDialog("manage-contact-modal");
        });
    });

    const appSwitch = document.getElementById("prototype-app-switch");

    if (appSwitch) {
        document.addEventListener("click", (event) => {
            if (!(event.target instanceof Node) || appSwitch.contains(event.target)) {
                return;
            }

            appSwitch.open = false;
        });

        appSwitch.addEventListener("keydown", (event) => {
            if (event.key === "Escape") {
                appSwitch.open = false;
            }
        });
    }

    const activityLogModal = document.getElementById("activity-log-modal");
    const activityLogTitle = document.getElementById("activity-log-modal-title");
    const activityLogContent = document.getElementById("activity-log-modal-content");

    document.querySelectorAll(".dfe-f-activity-log-open").forEach(button => {
        button.addEventListener("click", (event) => {
            event.preventDefault();

            if (!activityLogModal || !activityLogTitle || !activityLogContent) {
                return;
            }

            activityLogTitle.textContent = button.dataset.activityTitle ?? "Activity logs";
            const encodedLogs = button.dataset.activityLogs ?? "";

            try {
                activityLogContent.textContent = decodeURIComponent(encodedLogs);
            } catch {
                activityLogContent.textContent = encodedLogs;
            }

            openDialog("activity-log-modal");
        });
    });

    const addContactModal = document.getElementById("add-contact-modal");
    const managerEmailInput = document.getElementById("managerEmail");

    if (addContactModal && managerEmailInput?.closest(".govuk-form-group--error")) {
        openDialog("add-contact-modal");
    }

    const changePasswordModal = document.getElementById("change-password-modal");
    const changePasswordCurrentValue = document.getElementById("change-password-current-value");

    const loadCurrentPassword = async () => {
        if (!changePasswordCurrentValue) {
            return;
        }

        const passwordUrl = changePasswordModal?.dataset.passwordUrl ?? "";

        if (!passwordUrl) {
            changePasswordCurrentValue.value = "Unavailable";
            return;
        }

        changePasswordCurrentValue.value = "Loading…";

        try {
            const response = await fetch(passwordUrl, {
                headers: { Accept: "application/json" },
                credentials: "same-origin"
            });

            if (!response.ok) {
                throw new Error(`Unable to load password (${response.status}).`);
            }

            const payload = await response.json();
            const password = payload.password ?? payload.Password ?? "";
            changePasswordCurrentValue.value = password || "Not set";
        } catch {
            changePasswordCurrentValue.value = "Unavailable";
        }
    };

    document.querySelectorAll('[aria-controls="change-password-modal"]').forEach(trigger => {
        trigger.addEventListener("click", () => {
            loadCurrentPassword();
        });
    });

    const addConfigModal = document.getElementById("add-config-var-modal");
    const manageConfigModal = document.getElementById("manage-config-var-modal");
    const manageConfigKeyDisplay = document.getElementById("manage-config-var-key-display");
    const manageConfigKeyInput = document.getElementById("manage-config-var-key");
    const manageConfigRemoveKeyInput = document.getElementById("manage-config-var-remove-key");
    const manageConfigValueInput = document.getElementById("manage-config-var-value");
    const manageConfigValueHint = document.getElementById("manage-config-var-value-hint");
    const manageConfigRemoveButton = document.getElementById("manage-config-var-remove-button");
    const manageConfigRemoveForm = document.getElementById("manage-config-var-remove-form");
    const settingsSection = document.getElementById("prototype-app-settings");
    const addConfigKeyInput = document.getElementById("ConfigVarKey");

    const normaliseConfigKeyInput = (value, finalize = false) => {
        let normalised = String(value ?? "")
            .toLowerCase()
            .replace(/[\s\-.]+/g, "_")
            .replace(/[^a-z0-9_]/g, "")
            .replace(/_+/g, "_");

        // Keys must start with a letter — strip leading digits/underscores.
        normalised = normalised.replace(/^[^a-z]+/, "");

        if (finalize) {
            normalised = normalised.replace(/_+$/g, "");
        }

        return normalised;
    };

    if (addConfigKeyInput) {
        const applyKeyNormalisation = (finalize = false) => {
            const caret = addConfigKeyInput.selectionStart;
            const before = addConfigKeyInput.value;
            const after = normaliseConfigKeyInput(before, finalize);

            if (before === after) {
                return;
            }

            addConfigKeyInput.value = after;

            if (!finalize && typeof caret === "number") {
                const nextCaret = Math.min(caret, after.length);
                addConfigKeyInput.setSelectionRange(nextCaret, nextCaret);
            }
        };

        addConfigKeyInput.addEventListener("input", () => applyKeyNormalisation(false));
        addConfigKeyInput.addEventListener("blur", () => applyKeyNormalisation(true));
    }

    const loadManageConfigValue = async (configKey) => {
        if (!manageConfigValueInput || !configKey) {
            return;
        }

        const valueUrl = settingsSection?.dataset.settingValueUrl ?? "";
        manageConfigValueInput.value = "";
        manageConfigValueInput.disabled = true;

        if (!valueUrl) {
            manageConfigValueInput.disabled = false;
            return;
        }

        try {
            const response = await fetch(`${valueUrl}&key=${encodeURIComponent(configKey)}`, {
                headers: { Accept: "application/json" },
                credentials: "same-origin"
            });

            if (!response.ok) {
                throw new Error(`Unable to load app setting (${response.status}).`);
            }

            const payload = await response.json();
            manageConfigValueInput.value = payload.value ?? payload.Value ?? "";
        } catch {
            manageConfigValueInput.value = "";
        } finally {
            manageConfigValueInput.disabled = false;
        }
    };

    document.querySelectorAll(".dfe-f-manage-config-open").forEach(trigger => {
        trigger.addEventListener("click", async (event) => {
            event.preventDefault();

            const configKey = trigger.dataset.configKey ?? "";
            const isSecret = trigger.dataset.configSecret === "true";
            const isPassword = trigger.dataset.configPassword === "true";
            const isRemovable = trigger.dataset.configRemovable !== "false";

            if (manageConfigKeyDisplay) {
                manageConfigKeyDisplay.textContent = configKey;
            }

            if (manageConfigKeyInput) {
                manageConfigKeyInput.value = configKey;
            }

            if (manageConfigRemoveKeyInput) {
                manageConfigRemoveKeyInput.value = configKey;
            }

            if (manageConfigRemoveButton) {
                manageConfigRemoveButton.hidden = !isRemovable;
            }

            if (manageConfigRemoveForm) {
                manageConfigRemoveForm.hidden = !isRemovable;
            }

            if (manageConfigValueInput) {
                manageConfigValueInput.minLength = isPassword ? 3 : 0;
            }

            if (manageConfigValueHint) {
                manageConfigValueHint.textContent = isPassword
                    ? "Password for accessing the live prototype. Must be at least 3 characters."
                    : isSecret
                        ? "The current value is shown below. Change it to update the stored secret."
                        : `Up to ${manageConfigValueInput?.maxLength ?? 1024} characters.`;
            }

            openDialog("manage-config-var-modal");
            await loadManageConfigValue(configKey);
        });
    });

    const addConfigKeyError = addConfigModal?.querySelector("#ConfigVarKey")?.closest(".govuk-form-group--error");
    const addConfigValueError = addConfigModal?.querySelector("#ConfigVarValue")?.closest(".govuk-form-group--error");

    if (addConfigModal && (addConfigKeyError || addConfigValueError)) {
        openDialog("add-config-var-modal");
    }

    const manageConfigValueError = manageConfigModal?.querySelector("#manage-config-var-value")?.closest(".govuk-form-group--error");

    if (manageConfigModal && manageConfigValueError && manageConfigKeyInput?.value) {
        if (manageConfigKeyDisplay) {
            manageConfigKeyDisplay.textContent = manageConfigKeyInput.value;
        }

        openDialog("manage-config-var-modal");

        if (!manageConfigValueInput?.value) {
            loadManageConfigValue(manageConfigKeyInput.value);
        }
    }

    const syncStatusLabels = {
        pending: "Waiting to sync with Azure",
        syncing: "Syncing with Azure",
        synced: "Synced with Azure",
        failed: "Azure sync failed"
    };

    const setSyncIconState = (row, status) => {
        const icon = row.querySelector(".app-setting-sync");
        if (!icon) {
            return;
        }

        const normalised = status || "synced";
        icon.dataset.syncStatus = normalised;
        row.dataset.syncStatus = normalised;
        icon.hidden = normalised === "synced";
        icon.title = syncStatusLabels[normalised] ?? syncStatusLabels.synced;

        const label = icon.querySelector(".app-setting-sync__label");
        if (label) {
            label.textContent = syncStatusLabels[normalised] ?? syncStatusLabels.synced;
        }
    };

    const updateSettingValue = (row, setting) => {
        if (setting.isSecret || setting.displayValue == null) {
            return;
        }

        const valueCell = row.querySelector(".prototype-settings-table__value");
        if (!valueCell) {
            return;
        }

        const code = valueCell.querySelector("code");
        if (code) {
            code.textContent = setting.displayValue;
        } else {
            valueCell.textContent = setting.displayValue;
        }
    };

    const dismissSyncBannerIfNeeded = (syncComplete) => {
        if (!syncComplete) {
            return;
        }

        const banner = document.getElementById("prototype-status-banner");
        if (banner?.dataset.dismissWhenSynced === "true") {
            banner.hidden = true;
        }
    };

    const pollAppSettingsSync = () => {
        if (!settingsSection) {
            return;
        }

        const statusUrl = settingsSection.dataset.settingsStatusUrl ?? "";
        if (!statusUrl) {
            return;
        }

        let hideSyncedTimer = null;
        const recentlySynced = new Set();

        const refresh = async () => {
            try {
                const response = await fetch(statusUrl, {
                    headers: { Accept: "application/json" },
                    credentials: "same-origin"
                });

                if (!response.ok) {
                    return;
                }

                const payload = await response.json();
                const settings = payload.settings ?? [];
                const hasPendingSync = Boolean(payload.hasPendingSync);
                const syncComplete = Boolean(payload.syncComplete);

                settingsSection.dataset.hasPendingSync = hasPendingSync ? "true" : "false";
                dismissSyncBannerIfNeeded(syncComplete);

                settings.forEach((setting) => {
                    const key = setting.key ?? setting.Key;
                    if (!key) {
                        return;
                    }

                    const row = settingsSection.querySelector(`[data-setting-key="${CSS.escape(key)}"]`);
                    if (!row) {
                        return;
                    }

                    const previous = row.dataset.syncStatus || "synced";
                    const next = setting.syncStatus || setting.SyncStatus || "synced";

                    updateSettingValue(row, {
                        isSecret: setting.isSecret ?? setting.IsSecret,
                        displayValue: setting.displayValue ?? setting.DisplayValue
                    });

                    if (previous !== "synced" && next === "synced") {
                        setSyncIconState(row, "synced");
                        row.querySelector(".app-setting-sync")?.removeAttribute("hidden");
                        recentlySynced.add(key);

                        window.clearTimeout(hideSyncedTimer);
                        hideSyncedTimer = window.setTimeout(() => {
                            recentlySynced.forEach((syncedKey) => {
                                const syncedRow = settingsSection.querySelector(`[data-setting-key="${CSS.escape(syncedKey)}"]`);
                                if (syncedRow?.dataset.syncStatus === "synced") {
                                    const icon = syncedRow.querySelector(".app-setting-sync");
                                    if (icon) {
                                        icon.hidden = true;
                                    }
                                }
                            });
                            recentlySynced.clear();
                        }, 1600);
                        return;
                    }

                    setSyncIconState(row, next);
                });
            } catch {
                // Keep polling; transient failures should not break the page.
            }
        };

        refresh();
        window.setInterval(refresh, 3000);
    };

    pollAppSettingsSync();

    const processingModal = window.prototypeManagerProcessingModal;
    const isModifiedNavigationClick = (event) =>
        event.defaultPrevented ||
        event.button !== 0 ||
        event.metaKey ||
        event.ctrlKey ||
        event.shiftKey ||
        event.altKey;

    const showNavigationLoading = (title, message) => {
        processingModal?.show(title, message);
    };

    const sectionTabs = document.getElementById("prototype-section-tabs");

    if (sectionTabs) {
        sectionTabs.querySelectorAll(".dfe-c-fe-tabs__link").forEach(link => {
            link.addEventListener("click", (event) => {
                if (isModifiedNavigationClick(event)) {
                    return;
                }

                if (link.closest(".dfe-c-fe-tabs__item--active")) {
                    return;
                }

                const sectionLabel = link.textContent?.trim() || "section";
                showNavigationLoading("Loading", `Opening ${sectionLabel}…`);
            });
        });
    }

    document.querySelectorAll(".dfe-c-fe-prototype-switch__option[href]").forEach(link => {
        link.addEventListener("click", (event) => {
            if (isModifiedNavigationClick(event)) {
                return;
            }

            showNavigationLoading("Loading", "Switching prototype…");
        });
    });

    document.querySelectorAll("[data-copy-text]").forEach(button => {
        button.addEventListener("click", async () => {
            const text = button.getAttribute("data-copy-text") ?? "";
            if (!text) {
                return;
            }

            const originalLabel = button.textContent?.trim() || "Copy";

            try {
                if (navigator.clipboard?.writeText) {
                    await navigator.clipboard.writeText(text);
                } else {
                    const input = document.createElement("textarea");
                    input.value = text;
                    input.setAttribute("readonly", "");
                    input.style.position = "absolute";
                    input.style.left = "-9999px";
                    document.body.appendChild(input);
                    input.select();
                    document.execCommand("copy");
                    document.body.removeChild(input);
                }

                button.textContent = "Copied";
                window.setTimeout(() => {
                    button.textContent = originalLabel;
                }, 1600);
            } catch {
                button.textContent = "Copy failed";
                window.setTimeout(() => {
                    button.textContent = originalLabel;
                }, 1600);
            }
        });
    });
})();
