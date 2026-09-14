// ================================================================
// CHATAPP SERVICE WORKER
// ================================================================


// ================================================================
// PUSH EVENT
// ================================================================

self.addEventListener("push", event => {

    if (!event.data) {
        return;
    }


    let data;

    try {
        data =
            event.data.json();
    }
    catch {
        data = {
            title: "ChatApp",
            body: event.data.text()
        };
    }


    const title =
        data.title || "ChatApp";


    const options = {

        body:
            data.body ||
            "You have a new message.",

        icon:
            "/icon-192.png",

        badge:
            "/icon-192.png",

        data:
            data.data || {},

        actions:
            data.actions || [],

        requireInteraction:
            false
    };


    event.waitUntil(
        self.registration.showNotification(
            title,
            options
        )
    );
});


// ================================================================
// NOTIFICATION CLICK
// ================================================================

self.addEventListener(
    "notificationclick",
    event => {

        const action =
            event.action;

        const notification =
            event.notification;

        const data =
            notification.data || {};


        notification.close();


        // ========================================================
        // LIKE BUTTON
        // ========================================================

        if (action === "like") {

            if (!data.roomCode ||
                !data.messageId ||
                !data.userName) {

                return;
            }


            event.waitUntil(

                fetch(
                    "/api/notifications/like",
                    {
                        method: "POST",

                        headers: {
                            "Content-Type":
                                "application/json"
                        },

                        body:
                            JSON.stringify({
                                roomCode:
                                    data.roomCode,

                                messageId:
                                    data.messageId,

                                userName:
                                    data.userName
                            })
                    }
                )
                    .catch(() => {
                        // Ignore notification action failure.
                    })
            );

            return;
        }


        // ========================================================
        // OPEN CHAT
        // ========================================================

        if (!data.roomCode) {
            return;
        }


        const chatUrl =
            "/chat/" +
            encodeURIComponent(
                data.roomCode
            );


        event.waitUntil(

            clients
                .matchAll({
                    type: "window",
                    includeUncontrolled: true
                })
                .then(clientList => {

                    // Try to reuse an existing ChatApp tab.
                    for (const client of clientList) {

                        if (
                            "focus" in client &&
                            client.url.includes("/chat/")
                        ) {

                            if (
                                "navigate" in client
                            ) {
                                client.navigate(
                                    chatUrl
                                );
                            }

                            return client.focus();
                        }
                    }


                    // No existing tab.
                    if (
                        clients.openWindow
                    ) {
                        return clients.openWindow(
                            chatUrl
                        );
                    }


                    return undefined;
                })
        );
    }
);


// ================================================================
// SERVICE WORKER INSTALL
// ================================================================

self.addEventListener(
    "install",
    event => {

        self.skipWaiting();

    }
);


// ================================================================
// SERVICE WORKER ACTIVATE
// ================================================================

self.addEventListener(
    "activate",
    event => {

        event.waitUntil(
            self.clients.claim()
        );

    }
);