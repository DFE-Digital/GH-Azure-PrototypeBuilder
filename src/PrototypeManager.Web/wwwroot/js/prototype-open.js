(() => {
    const startup = document.getElementById("prototype-startup");

    if (!startup) {
        return;
    }

    const readyUrl = startup.dataset.readyUrl ?? "";
    const redirectUrl = startup.dataset.redirectUrl ?? "";
    const stateElement = document.getElementById("prototype-startup-state");
    const messageElement = document.getElementById("prototype-startup-message");
    const pollIntervalMs = 2500;
    let attempts = 0;

    const updateState = (powerState) => {
        if (!stateElement || !powerState) {
            return;
        }

        stateElement.textContent = `App state: ${powerState}`;
    };

    const poll = async () => {
        attempts += 1;

        try {
            const response = await fetch(readyUrl, {
                headers: { Accept: "application/json" }
            });

            if (!response.ok) {
                throw new Error(`Ready check failed (${response.status})`);
            }

            const payload = await response.json();

            if (payload.powerState) {
                updateState(payload.powerState);
            }

            if (payload.ready) {
                window.location.assign(payload.redirectUrl || redirectUrl);
                return;
            }
        } catch {
            if (messageElement) {
                messageElement.textContent = "Still starting your prototype. Retrying…";
            }
        }

        if (attempts >= 60 && messageElement) {
            messageElement.textContent = "This is taking longer than usual. We will keep trying, or you can go back and try again later.";
        }

        window.setTimeout(poll, pollIntervalMs);
    };

    poll();
})();
