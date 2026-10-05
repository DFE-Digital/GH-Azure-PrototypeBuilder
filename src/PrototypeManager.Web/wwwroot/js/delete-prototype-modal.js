(() => {
    const openDialog = (id) => {
        const dialog = document.getElementById(id);

        if (dialog instanceof HTMLDialogElement && typeof dialog.showModal === "function") {
            dialog.showModal();
        }
    };

    const form = document.getElementById("delete-prototype-form");
    const modal = document.getElementById("delete-prototype-modal");
    const openButton = document.getElementById("delete-prototype-open");

    if (!form || !modal || !openButton) {
        return;
    }

    const prototypeName = form.dataset.prototypeName ?? "";
    const deletesGithubRepo = form.dataset.deletesGithubRepo === "true";
    const input = document.getElementById("delete-prototype-modal-input");
    const submitButton = document.getElementById("delete-prototype-modal-submit");
    const errorMessage = document.getElementById("delete-prototype-modal-error");
    const formGroup = document.getElementById("delete-prototype-modal-form-group");
    const nameElement = document.getElementById("delete-prototype-modal-name");
    const nameHint = document.getElementById("delete-prototype-modal-name-hint");
    const githubNote = document.getElementById("delete-prototype-modal-github-note");

    const namesMatch = (value) => value.trim() === prototypeName;

    const setErrorVisible = (visible) => {
        if (!errorMessage || !formGroup) {
            return;
        }

        errorMessage.hidden = !visible;
        formGroup.classList.toggle("govuk-form-group--error", visible);

        if (input) {
            input.classList.toggle("govuk-input--error", visible);
            input.setAttribute("aria-invalid", visible ? "true" : "false");
        }
    };

    const updateSubmitState = () => {
        if (!input || !submitButton) {
            return;
        }

        const matches = namesMatch(input.value);
        submitButton.disabled = !matches;

        if (matches) {
            setErrorVisible(false);
        }
    };

    openButton.addEventListener("click", () => {
        if (nameElement) {
            nameElement.textContent = prototypeName;
        }

        if (nameHint) {
            nameHint.textContent = prototypeName;
        }

        if (githubNote) {
            githubNote.hidden = !deletesGithubRepo;
        }

        if (input) {
            input.value = "";
            input.classList.remove("govuk-input--error");
        }

        setErrorVisible(false);

        if (submitButton) {
            submitButton.disabled = true;
        }

        openDialog("delete-prototype-modal");
    });

    input?.addEventListener("input", updateSubmitState);

    submitButton?.addEventListener("click", (event) => {
        if (!input || namesMatch(input.value)) {
            return;
        }

        event.preventDefault();
        setErrorVisible(true);
        input.focus();
    });

    form.addEventListener("submit", (event) => {
        if (!input || namesMatch(input.value)) {
            return;
        }

        event.preventDefault();
        setErrorVisible(true);
        input.focus();
    });
})();
