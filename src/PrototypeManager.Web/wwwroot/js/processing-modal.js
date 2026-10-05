(() => {
    const modal = document.getElementById("processing-modal");
    if (!modal) {
        return;
    }

    const titleElement = document.getElementById("processing-modal-title");
    const messageElement = document.getElementById("processing-modal-message");
    let shownForBackgroundJob = false;

    const show = (title, message, forBackgroundJob = false) => {
        if (titleElement && title) {
            titleElement.textContent = title;
        }

        if (messageElement) {
            messageElement.textContent = message || "Please wait…";
        }

        modal.hidden = false;
        document.body.classList.add("processing-modal-open");

        if (forBackgroundJob) {
            shownForBackgroundJob = true;
        }
    };

    const update = (title, message) => {
        if (modal.hidden) {
            show(title, message, true);
            return;
        }

        if (titleElement && title) {
            titleElement.textContent = title;
        }

        if (messageElement && message) {
            messageElement.textContent = message;
        }
    };

    const hide = () => {
        modal.hidden = true;
        document.body.classList.remove("processing-modal-open");
        shownForBackgroundJob = false;
    };

    window.prototypeManagerProcessingModal = {
        show,
        update,
        hide,
        isVisible: () => !modal.hidden,
        wasShownForBackgroundJob: () => shownForBackgroundJob
    };

    document.querySelectorAll("form.processing-trigger, form.prototype-action-form").forEach(form => {
        form.addEventListener("submit", () => {
            const submitButton = form.querySelector("govuk-button, button[type='submit']");
            const label = form.dataset.processingTitle
                || submitButton?.textContent?.trim()
                || "Processing";

            show(
                label,
                form.dataset.processingMessage || "This may take a minute. Do not close this page.");
        });
    });
})();
