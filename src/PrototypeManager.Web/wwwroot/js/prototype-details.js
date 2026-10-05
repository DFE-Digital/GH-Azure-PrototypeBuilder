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

    const changeExpiryModal = document.getElementById("change-expiry-modal");
    const expiryDateGroup = changeExpiryModal?.querySelector(".govuk-form-group--error");

    if (changeExpiryModal && expiryDateGroup) {
        openDialog("change-expiry-modal");
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
})();
