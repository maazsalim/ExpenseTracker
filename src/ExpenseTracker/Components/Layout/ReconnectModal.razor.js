// Quiet reconnection. Blazor fires "components-reconnect-state-changed" on this element when the
// live connection drops (for example iOS pausing the installed app in the background) and when
// it comes back. Short drops reconnect silently; if the server no longer has the session, the
// page reloads instead of asking the user to press Retry.

const pill = document.getElementById("components-reconnect-modal");
let showTimer;

pill.addEventListener("components-reconnect-state-changed", event => {
    switch (event.detail.state) {
        case "show":          // reconnecting
        case "retrying":
            showPillSoon();
            break;
        case "hide":          // reconnected
            hidePill();
            break;
        case "failed":        // gave up retrying: the server isn't reachable right now
            reloadWhenVisible();
            break;
        case "rejected":      // the server is reachable but the session has gone
        case "resume-failed":
            location.reload();
            break;
        case "paused":        // the server paused the session: pick it back up
            resume();
            break;
    }
});

// Most reconnects take well under a second; only show the pill if it's taking a while.
// Blazor fires "retrying" on every attempt, so start the timer once rather than restarting it.
function showPillSoon() {
    if (showTimer || pill.open)
        return;
    showTimer = setTimeout(() => {
        showTimer = undefined;
        pill.show();
    }, 1500);
}

function hidePill() {
    clearTimeout(showTimer);
    showTimer = undefined;
    pill.close();
}

// Reload now if the app is on screen, otherwise as soon as it's opened again.
// (Reloading also wakes the free host if it was asleep.)
function reloadWhenVisible() {
    if (document.visibilityState === "visible") {
        location.reload();
    } else {
        document.addEventListener("visibilitychange", reloadWhenVisible, { once: true });
    }
}

async function resume() {
    try {
        if (await Blazor.resumeCircuit()) {
            hidePill();
            return;
        }
    } catch {
        // fall through to a reload
    }
    location.reload();
}
