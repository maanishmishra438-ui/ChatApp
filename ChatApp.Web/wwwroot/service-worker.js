// Service Worker for ChatApp
// Handles browser push notifications and notification click routing.

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
        self.registration.showNotification(title, options)
    );
});


self.addEventListener("notificationclick", (event) => {
    event.notification.close();

    const data = event.notification.data || {};
    const action = event.action || "open";


    // ---------------------------------------------
    // LIKE ACTION
    // ---------------------------------------------
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
            }).catch(() => {
                // Notification actions must never break
                // the service worker.
            })
        );

        return;
    }


    // ---------------------------------------------
    // OPEN CHAT ACTION
    // ---------------------------------------------
    if (!data.roomCode) {
        return;
    }


    // This marker tells Chat.razor that the navigation
    // came from a push notification.
    //
    // Normal invite URLs such as:
    // /chat/ABC123
    //
    // remain unchanged and will still ask for the name.
    const chatUrl =
        "/chat/" +
        encodeURIComponent(data.roomCode) +
        "?notification=1";


    event.waitUntil(
        clients.matchAll({
            type: "window",
            includeUncontrolled: true
        }).then((windowClients) => {

            // Try to reuse an already-open ChatApp tab.
            for (const client of windowClients) {

                if (
                    client.url.includes("/chat/") &&
                    "focus" in client
                ) {
                    return client
                        .navigate(chatUrl)
                        .then(() => client.focus());
                }
            }


            // No existing ChatApp window.
            // Open a new one.
            if (clients.openWindow) {
                return clients.openWindow(chatUrl);
            }

            return undefined;
        })
    );
});


self.addEventListener("install", () => {
    self.skipWaiting();
});


self.addEventListener("activate", (event) => {
    event.waitUntil(
        clients.claim()
    );
});