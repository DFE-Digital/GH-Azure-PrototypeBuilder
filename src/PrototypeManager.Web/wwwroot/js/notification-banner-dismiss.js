(() => {
    const enhanceBanner = (banner) => {
        if (banner.dataset.dismissible === "false") {
            return;
        }

        if (banner.querySelector("[data-app-banner-dismiss]")) {
            return;
        }

        const dismiss = document.createElement("button");
        dismiss.type = "button";
        dismiss.className = "govuk-link app-notification-banner__dismiss";
        dismiss.setAttribute("data-app-banner-dismiss", "");
        dismiss.setAttribute("aria-label", "Dismiss notification");
        dismiss.textContent = "Dismiss";
        dismiss.addEventListener("click", () => {
            banner.hidden = true;
            banner.setAttribute("aria-hidden", "true");
        });

        const header = banner.querySelector(".govuk-notification-banner__header");
        if (header) {
            header.classList.add("app-notification-banner__header");
            header.appendChild(dismiss);
            return;
        }

        const content = banner.querySelector(".govuk-notification-banner__content") || banner;
        const paragraph = document.createElement("p");
        paragraph.className = "govuk-body govuk-!-margin-bottom-0 govuk-!-margin-top-2";
        paragraph.appendChild(dismiss);
        content.appendChild(paragraph);
    };

    const enhanceAll = () => {
        document.querySelectorAll(".govuk-notification-banner").forEach(enhanceBanner);
    };

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", enhanceAll);
    } else {
        enhanceAll();
    }
})();
