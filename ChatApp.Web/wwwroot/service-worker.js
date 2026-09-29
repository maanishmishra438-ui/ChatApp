// Service Worker for ChatApp
// Handles browser push notifications and notification click routing.


// ============================================================
// PUSH NOTIFICATION
// ============================================================

self.addEventListener("push", (event) => {
    if (!event.data) {
        return;
    }

    let data = {};

    try {
        data = event.data.json();
    } catch {
        data = {
            title: "New message",
            body: event.data.text()
        };
    }

    const notificationData = data.data || {};

    const title = data.title || "ChatApp";

    const options = {
        body: data.body || "You have a new message.",

        icon: data.icon || "/icon-192.png",

        badge: data.badge || "/icon-192.png",

        // IMPORTANT:
        // roomCode, messageId, userName etc.
        // are stored here and are available inside
        // notificationclick.
        data: notificationData,

        actions: data.actions || [
            {
                action: "open",
                title: "Open"
            },
            {
                action: "like",
                title: "Like"
            }
        ]
    };

    event.waitUntil(
        self.registration.showNotification(
            title,
            options
        )
    );
});


// ============================================================
// NOTIFICATION CLICK
// ============================================================

self.addEventListener("notificationclick", (event) => {

    // Close the notification immediately.
    event.notification.close();

    const data =
        event.notification.data || {};

    const action =
        event.action || "open";


    // ========================================================
    // LIKE ACTION
    // ========================================================

    if (action === "like") {

        event.waitUntil(
            fetch("/api/notifications/like", {
                method: "POST",

                headers: {
                    "Content-Type": "application/json"
                },

                body: JSON.stringify({
                    roomCode: data.roomCode,
                    messageId: data.messageId,
                    userName: data.userName
                })
            }).catch((error) => {

                // Never allow a notification action
                // to crash the service worker.
                console.error(
                    "ChatApp notification like failed:",
                    error
                );
            })
        );

        return;
    }


    // ========================================================
    // OPEN CHAT ACTION
    // ========================================================

    // Without roomCode there is nowhere to navigate.
    if (!data.roomCode) {

        console.warn(
            "ChatApp notification has no roomCode."
        );

        return;
    }


    // ========================================================
    // BUILD CHAT URL
    // ========================================================

    // IMPORTANT:
    //
    // Normal invite:
    //
    // /chat/ROOMCODE
    //
    // Notification:
    //
    // /chat/ROOMCODE?notification=1
    //
    // Chat.razor uses notification=1 to know that
    // this navigation came from a push notification.
    //
    // The absolute URL is intentionally used so browser
    // navigation is reliable.

    const chatUrl =
        new URL(
            `/chat/${encodeURIComponent(data.roomCode)}?notification=1`,
            self.location.origin
        ).href;


    console.log(
        "ChatApp notification opening:",
        chatUrl
    );


    // ========================================================
    // FIND / REUSE EXISTING BROWSER WINDOW
    // ========================================================

    event.waitUntil(

        clients.matchAll({
            type: "window",
            includeUncontrolled: true
        })

            .then(async (windowClients) => {

                // ------------------------------------------------
                // First try an already-open ChatApp window.
                // ------------------------------------------------

                for (const client of windowClients) {

                    if (!("focus" in client)) {
                        continue;
                    }

                    try {

                        // Navigate the existing tab directly
                        // to the correct chat room.
                        await client.navigate(chatUrl);

                        // Bring that tab to the foreground.
                        await client.focus();

                        console.log(
                            "ChatApp existing window focused."
                        );

                        return;

                    } catch (error) {

                        console.error(
                            "Could not navigate existing ChatApp window:",
                            error
                        );
                    }
                }


                // ------------------------------------------------
                // No existing ChatApp window.
                // Open a new browser tab/window.
                // ------------------------------------------------

                if ("openWindow" in clients) {

                    try {

                        await clients.openWindow(
                            chatUrl
                        );

                        console.log(
                            "ChatApp new window opened."
                        );

                    } catch (error) {

                        console.error(
                            "Could not open ChatApp window:",
                            error
                        );
                    }
                }
            })

            .catch((error) => {

                console.error(
                    "ChatApp notification navigation failed:",
                    error
                );
            })
    );
});


// ============================================================
// SERVICE WORKER INSTALL
// ============================================================

self.addEventListener("install", () => {

    // Activate the new service worker immediately.
    self.skipWaiting();
});


// ============================================================
// SERVICE WORKER ACTIVATE
// ============================================================

self.addEventListener("activate", (event) => {

    event.waitUntil(

        // Take control of already-open pages.
        clients.claim()

    );
});