// ================================================================
// CHATAPP PUSH NOTIFICATIONS
// ================================================================
//
// Handles:
// - Browser notification permission
// - Push subscription
// - VAPID subscription
// - ChatApp browser-tab tracking
// - Exact notification tab routing
//
// UI is not touched here.
// ================================================================


// ================================================================
// ENABLE NOTIFICATIONS
// ================================================================

export async function enable(
    userName,
    roomCode
) {

    // ------------------------------------------------------------
    // Browser support
    // ------------------------------------------------------------

    if (!window.isSecureContext) {

        throw new Error(
            "Notifications require a secure HTTPS context."
        );
    }


    if (!("serviceWorker" in navigator)) {

        throw new Error(
            "Service Worker is not supported by this browser."
        );
    }


    if (!("PushManager" in window)) {

        throw new Error(
            "Push API is not supported by this browser."
        );
    }


    if (!("Notification" in window)) {

        throw new Error(
            "Notification API is not supported by this browser."
        );
    }


    // ------------------------------------------------------------
    // Permission
    // ------------------------------------------------------------

    const permission =
        await Notification.requestPermission();


    if (
        permission !==
        "granted"
    ) {

        throw new Error(
            `Notification permission is "${permission}".`
        );
    }


    // ------------------------------------------------------------
    // Service worker
    // ------------------------------------------------------------

    const registration =
        await navigator.serviceWorker.register(
            "/service-worker.js",
            {
                scope: "/"
            }
        );


    const readyRegistration =
        await navigator.serviceWorker.ready;


    // ------------------------------------------------------------
    // VAPID public key
    // ------------------------------------------------------------

    const keyResponse =
        await fetch(
            "/api/notifications/public-key",
            {
                cache: "no-store"
            }
        );


    if (!keyResponse.ok) {

        throw new Error(
            `VAPID endpoint returned HTTP ${keyResponse.status}.`
        );
    }


    const keyData =
        await keyResponse.json();


    if (!keyData?.publicKey) {

        throw new Error(
            "VAPID public key is missing."
        );
    }


    const applicationServerKey =
        urlBase64ToUint8Array(
            keyData.publicKey
        );


    console.log(
        "VAPID public key bytes:",
        applicationServerKey.length
    );


    if (
        applicationServerKey.length !==
        65
    ) {

        throw new Error(
            `Invalid VAPID public key length: ${applicationServerKey.length}. Expected 65.`
        );
    }


    // ------------------------------------------------------------
    // Existing subscription
    // ------------------------------------------------------------

    let subscription =
        await readyRegistration.pushManager
            .getSubscription();


    // ------------------------------------------------------------
    // Create subscription
    // ------------------------------------------------------------

    if (!subscription) {

        try {

            console.log(
                "Calling PushManager.subscribe..."
            );


            subscription =
                await readyRegistration
                    .pushManager
                    .subscribe({
                        userVisibleOnly: true,
                        applicationServerKey
                    });


            console.log(
                "Push subscription created successfully."
            );

        }
        catch (error) {

            console.error(
                "========== PUSH SUBSCRIBE ERROR =========="
            );

            console.error(
                "Error object:",
                error
            );

            console.error(
                "Name:",
                error?.name
            );

            console.error(
                "Message:",
                error?.message
            );

            console.error(
                "Code:",
                error?.code
            );

            console.error(
                "Stack:",
                error?.stack
            );

            console.error(
                "=========================================="
            );


            throw new Error(
                "Push subscription failed: " +
                formatError(error)
            );
        }
    }


    // ------------------------------------------------------------
    // Convert subscription
    // ------------------------------------------------------------

    const json =
        subscription.toJSON();


    if (
        !json?.endpoint ||
        !json?.keys?.p256dh ||
        !json?.keys?.auth
    ) {

        throw new Error(
            "Browser returned an incomplete push subscription."
        );
    }


    console.log(
        "Push endpoint:",
        json.endpoint
    );


    // ------------------------------------------------------------
    // Save to backend
    // ------------------------------------------------------------

    const saveResponse =
        await fetch(
            "/api/notifications/subscribe",
            {
                method: "POST",

                headers: {
                    "Content-Type":
                        "application/json"
                },

                body:
                    JSON.stringify({

                        userName,

                        roomCode,

                        endpoint:
                            json.endpoint,

                        p256dh:
                            json.keys.p256dh,

                        auth:
                            json.keys.auth
                    })
            }
        );


    if (!saveResponse.ok) {

        const responseText =
            await saveResponse.text();


        throw new Error(
            `Subscription API returned ${saveResponse.status}: ${responseText}`
        );
    }


    // ------------------------------------------------------------
    // Register this browser tab with service worker.
    // ------------------------------------------------------------

    await registerChatTab(
        roomCode
    );


    return true;
}


// ================================================================
// REGISTER CURRENT CHAT TAB
// ================================================================
//
// This can safely be called repeatedly.
//
// The service worker stores:
//     roomCode -> client.id
//
// This lets notification clicks target the correct existing tab.
// ================================================================

export async function registerChatTab(
    roomCode
) {

    if (
        !roomCode ||
        typeof roomCode !== "string"
    ) {

        return false;
    }


    if (
        !("serviceWorker" in navigator)
    ) {

        return false;
    }


    try {

        const registration =
            await navigator.serviceWorker.ready;


        const worker =
            registration.active ||
            navigator.serviceWorker.controller;


        if (!worker) {

            return false;
        }


        worker.postMessage({

            type:
                "CHATAPP_REGISTER_TAB",

            roomCode:
                roomCode
        });


        return true;

    }
    catch (error) {

        console.warn(
            "ChatApp tab registration failed:",
            error
        );

        return false;
    }
}


// ================================================================
// UNREGISTER CURRENT CHAT TAB
// ================================================================

export async function unregisterChatTab() {

    if (
        !("serviceWorker" in navigator)
    ) {

        return false;
    }


    try {

        const registration =
            await navigator.serviceWorker.ready;


        const worker =
            registration.active ||
            navigator.serviceWorker.controller;


        if (!worker) {

            return false;
        }


        worker.postMessage({

            type:
                "CHATAPP_UNREGISTER_TAB"
        });


        return true;

    }
    catch {

        return false;
    }
}


// ================================================================
// TRACK CURRENT CHAT TAB
// ================================================================
//
// Reads the current URL:
//
// /chat/ABC123
//
// and tells the service worker which room this tab currently
// represents.
//
// Focus/visibility changes are also tracked so if multiple tabs
// exist for the same room, the latest active tab becomes the
// preferred target.
// ================================================================

async function trackCurrentChatTab() {

    try {

        const pathname =
            window.location.pathname;


        const match =
            pathname.match(
                /^\/chat\/([^/]+)\/?$/
            );


        if (!match) {

            return;
        }


        const roomCode =
            decodeURIComponent(
                match[1]
            );


        if (!roomCode) {

            return;
        }


        await registerChatTab(
            roomCode
        );

    }
    catch (error) {

        console.warn(
            "ChatApp current tab tracking failed:",
            error
        );
    }
}


// ================================================================
// TRACK WHEN TAB BECOMES ACTIVE
// ================================================================

function setupTabTracking() {

    if (
        window.__chatAppTabTrackingInstalled
    ) {

        return;
    }


    window.__chatAppTabTrackingInstalled =
        true;


    // ------------------------------------------------------------
    // Initial registration.
    // ------------------------------------------------------------

    void trackCurrentChatTab();


    // ------------------------------------------------------------
    // Browser tab becomes visible.
    // ------------------------------------------------------------

    document.addEventListener(
        "visibilitychange",
        () => {

            if (
                document.visibilityState ===
                "visible"
            ) {

                void trackCurrentChatTab();
            }
        }
    );


    // ------------------------------------------------------------
    // Browser window receives focus.
    // ------------------------------------------------------------

    window.addEventListener(
        "focus",
        () => {

            void trackCurrentChatTab();
        },
        {
            passive: true
        }
    );


    // ------------------------------------------------------------
    // Before leaving page/tab.
    // ------------------------------------------------------------

    window.addEventListener(
        "pagehide",
        () => {

            void unregisterChatTab();
        },
        {
            passive: true
        }
    );
}


// ================================================================
// START TAB TRACKING
// ================================================================

setupTabTracking();


// ================================================================
// BASE64URL → UINT8ARRAY
// ================================================================

function urlBase64ToUint8Array(
    base64String
) {

    const padding =
        "=".repeat(
            (
                4 -
                base64String.length % 4
            ) % 4
        );


    const base64 =
        (
            base64String +
            padding
        )
            .replace(
                /-/g,
                "+"
            )
            .replace(
                /_/g,
                "/"
            );


    const rawData =
        window.atob(
            base64
        );


    return Uint8Array.from(
        [...rawData].map(
            char =>
                char.charCodeAt(0)
        )
    );
}


// ================================================================
// ERROR FORMATTER
// ================================================================

function formatError(
    error
) {

    if (!error) {

        return "Unknown browser error.";
    }


    if (
        error.name &&
        error.message
    ) {

        return `${error.name}: ${error.message}`;
    }


    if (error.message) {

        return error.message;
    }


    return String(error);
}