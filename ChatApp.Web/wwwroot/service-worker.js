// Service Worker for ChatApp
// Handles browser push notifications and exact ChatApp tab routing.


// ============================================================
// CHAT TAB TRACKING
// ============================================================
//
// Each ChatApp browser tab sends its service-worker client id
// together with the room it is currently viewing.
//
// This allows notification clicks to focus the correct tab
// instead of blindly navigating the first browser tab found.
//

const chatTabRegistry = new Map();
// roomCode -> clientId


// ============================================================
// NORMALIZE ROOM CODE
// ============================================================

function normalizeRoomCode(roomCode) {

    if (!roomCode) {
        return "";
    }

    return String(roomCode).trim();
}


// ============================================================
// CHECK WHETHER CLIENT BELONGS TO CHATAPP
// ============================================================

function isChatAppClient(client) {

    if (!client || !client.url) {
        return false;
    }

    try {

        const url =
            new URL(
                client.url,
                self.location.origin
            );

        return url.origin === self.location.origin;

    }
    catch {

        return false;
    }
}


// ============================================================
// EXTRACT ROOM CODE FROM CHAT URL
// ============================================================

function getRoomCodeFromUrl(urlString) {

    if (!urlString) {
        return "";
    }

    try {

        const url =
            new URL(
                urlString,
                self.location.origin
            );

        const match =
            url.pathname.match(
                /^\/chat\/([^/]+)\/?$/
            );

        if (!match) {
            return "";
        }

        return decodeURIComponent(
            match[1]
        );

    }
    catch {

        return "";
    }
}


// ============================================================
// REMOVE CLIENT FROM REGISTRY
// ============================================================

function removeClientFromRegistry(clientId) {

    if (!clientId) {
        return;
    }

    for (
        const [roomCode, registeredClientId]
        of chatTabRegistry.entries()
    ) {

        if (
            registeredClientId === clientId
        ) {

            chatTabRegistry.delete(
                roomCode
            );
        }
    }
}


// ============================================================
// REGISTER / UPDATE CHAT TAB
// ============================================================

async function registerChatTab(
    clientId,
    roomCode
) {

    if (!clientId) {
        return;
    }

    const normalizedRoomCode =
        normalizeRoomCode(roomCode);

    if (!normalizedRoomCode) {
        return;
    }


    // --------------------------------------------------------
    // Remove previous room association for this client.
    // --------------------------------------------------------

    removeClientFromRegistry(
        clientId
    );


    // --------------------------------------------------------
    // Store latest active tab for this room.
    // --------------------------------------------------------

    chatTabRegistry.set(
        normalizedRoomCode,
        clientId
    );


    console.log(
        "ChatApp tab registered:",
        normalizedRoomCode,
        clientId
    );


    // --------------------------------------------------------
    // Clean stale clients.
    // --------------------------------------------------------

    try {

        const client =
            await clients.get(
                clientId
            );

        if (!client) {

            removeClientFromRegistry(
                clientId
            );
        }

    }
    catch {

        removeClientFromRegistry(
            clientId
        );
    }
}


// ============================================================
// PUSH NOTIFICATION
// ============================================================

self.addEventListener(
    "push",
    (event) => {

        if (!event.data) {
            return;
        }

        let data = {};

        try {

            data =
                event.data.json();

        }
        catch {

            data = {
                title: "New message",
                body: event.data.text()
            };
        }


        const notificationData =
            data.data || {};


        const title =
            data.title || "ChatApp";


        const options = {

            body:
                data.body ||
                "You have a new message.",

            icon:
                data.icon ||
                "/icon-192.png",

            badge:
                data.badge ||
                "/icon-192.png",

            // roomCode, messageId, userName etc.
            // are stored here and available
            // inside notificationclick.
            data:
                notificationData,

            actions:
                data.actions ||
                [
                    {
                        action: "open",
                        title: "Open chat"
                    },
                    {
                        action: "like",
                        title: "❤️ Like"
                    }
                ]
        };


        event.waitUntil(

            self.registration.showNotification(
                title,
                options
            )
        );
    }
);


// ============================================================
// MESSAGE FROM CHAT TAB
// ============================================================

self.addEventListener(
    "message",
    (event) => {

        const message =
            event.data;

        if (!message) {
            return;
        }


        // ----------------------------------------------------
        // Register / update active ChatApp tab.
        // ----------------------------------------------------

        if (
            message.type ===
            "CHATAPP_REGISTER_TAB"
        ) {

            const clientId =
                event.source?.id ||
                message.clientId;

            const roomCode =
                message.roomCode;


            event.waitUntil(

                registerChatTab(
                    clientId,
                    roomCode
                )
            );

            return;
        }


        // ----------------------------------------------------
        // Unregister ChatApp tab.
        // ----------------------------------------------------

        if (
            message.type ===
            "CHATAPP_UNREGISTER_TAB"
        ) {

            const clientId =
                event.source?.id ||
                message.clientId;

            removeClientFromRegistry(
                clientId
            );

            console.log(
                "ChatApp tab unregistered:",
                clientId
            );

            return;
        }
    }
);


// ============================================================
// FIND EXACT ROOM TAB
// ============================================================

async function findExactRoomClient(
    roomCode
) {

    const normalizedRoomCode =
        normalizeRoomCode(
            roomCode
        );

    if (!normalizedRoomCode) {
        return null;
    }


    // --------------------------------------------------------
    // First use our tracked client.
    // --------------------------------------------------------

    const trackedClientId =
        chatTabRegistry.get(
            normalizedRoomCode
        );


    if (trackedClientId) {

        try {

            const trackedClient =
                await clients.get(
                    trackedClientId
                );


            if (
                trackedClient &&
                isChatAppClient(
                    trackedClient
                )
            ) {

                const trackedRoomCode =
                    getRoomCodeFromUrl(
                        trackedClient.url
                    );


                if (
                    trackedRoomCode ===
                    normalizedRoomCode
                ) {

                    return trackedClient;
                }
            }

        }
        catch {
        }


        // Stale mapping.
        chatTabRegistry.delete(
            normalizedRoomCode
        );
    }


    // --------------------------------------------------------
    // Fallback:
    // Search every existing browser client.
    // --------------------------------------------------------

    const windowClients =
        await clients.matchAll({
            type: "window",
            includeUncontrolled: true
        });


    const matchingClients =
        windowClients.filter(
            client => {

                if (
                    !isChatAppClient(
                        client
                    )
                ) {
                    return false;
                }

                const clientRoomCode =
                    getRoomCodeFromUrl(
                        client.url
                    );

                return (
                    clientRoomCode ===
                    normalizedRoomCode
                );
            }
        );


    if (
        matchingClients.length ===
        0
    ) {

        return null;
    }


    // --------------------------------------------------------
    // Prefer a currently focused/visible client.
    // --------------------------------------------------------

    const focusedClient =
        matchingClients.find(
            client =>
                client.focused === true
        );


    if (focusedClient) {

        chatTabRegistry.set(
            normalizedRoomCode,
            focusedClient.id
        );

        return focusedClient;
    }


    const visibleClient =
        matchingClients.find(
            client =>
                client.visibilityState ===
                "visible"
        );


    if (visibleClient) {

        chatTabRegistry.set(
            normalizedRoomCode,
            visibleClient.id
        );

        return visibleClient;
    }


    // --------------------------------------------------------
    // clients.matchAll() normally gives the most recently
    // focused WindowClient first.
    // --------------------------------------------------------

    const fallbackClient =
        matchingClients[0];


    if (fallbackClient) {

        chatTabRegistry.set(
            normalizedRoomCode,
            fallbackClient.id
        );
    }


    return fallbackClient || null;
}


// ============================================================
// FIND ANY CHATAPP WINDOW
// ============================================================

async function findAnyChatAppClient() {

    const windowClients =
        await clients.matchAll({
            type: "window",
            includeUncontrolled: true
        });


    for (
        const client
        of windowClients
    ) {

        if (
            isChatAppClient(
                client
            )
        ) {

            return client;
        }
    }


    return null;
}


// ============================================================
// BUILD CHAT URL
// ============================================================

function buildChatUrl(
    roomCode
) {

    return new URL(

        `/chat/${encodeURIComponent(
            roomCode
        )}?notification=1`,

        self.location.origin

    ).href;
}


// ============================================================
// NOTIFICATION CLICK
// ============================================================

self.addEventListener(
    "notificationclick",
    (event) => {

        // Close notification immediately.
        event.notification.close();


        const data =
            event.notification.data ||
            {};


        const action =
            event.action ||
            "open";


        // ====================================================
        // LIKE ACTION
        // ====================================================

        if (
            action ===
            "like"
        ) {

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

                    .catch(
                        (error) => {

                            console.error(
                                "ChatApp notification like failed:",
                                error
                            );
                        }
                    )
            );

            return;
        }


        // ====================================================
        // OPEN CHAT ACTION
        // ====================================================

        if (!data.roomCode) {

            console.warn(
                "ChatApp notification has no roomCode."
            );

            return;
        }


        const roomCode =
            normalizeRoomCode(
                data.roomCode
            );


        if (!roomCode) {
            return;
        }


        const chatUrl =
            buildChatUrl(
                roomCode
            );


        console.log(
            "ChatApp notification opening room:",
            roomCode
        );


        // ====================================================
        // ROUTING
        // ====================================================

        event.waitUntil(

            (async () => {

                // ------------------------------------------------
                // 1. Find exact room tab.
                // ------------------------------------------------

                const exactClient =
                    await findExactRoomClient(
                        roomCode
                    );


                if (exactClient) {

                    console.log(
                        "ChatApp exact room tab found:",
                        exactClient.id,
                        exactClient.url
                    );


                    // IMPORTANT:
                    //
                    // If the tab is already on the exact room,
                    // DO NOT navigate it.
                    //
                    // This prevents the page refresh/reload
                    // problem.
                    //

                    try {

                        const currentRoomCode =
                            getRoomCodeFromUrl(
                                exactClient.url
                            );


                        if (
                            currentRoomCode ===
                            roomCode
                        ) {

                            await exactClient.focus();

                            console.log(
                                "ChatApp exact room tab focused without navigation."
                            );

                            return;
                        }

                    }
                    catch {
                    }


                    // ------------------------------------------------
                    // The tracked ChatApp tab exists but is currently
                    // somewhere else. Navigate only in this case.
                    // ------------------------------------------------

                    try {

                        await exactClient.navigate(
                            chatUrl
                        );

                        await exactClient.focus();

                        chatTabRegistry.set(
                            roomCode,
                            exactClient.id
                        );

                        console.log(
                            "ChatApp tab navigated to notification room."
                        );

                        return;

                    }
                    catch (error) {

                        console.error(
                            "Could not navigate exact ChatApp tab:",
                            error
                        );
                    }
                }


                // ------------------------------------------------
                // 2. No exact room tab.
                //
                // Try an already-open ChatApp window.
                // ------------------------------------------------

                const existingChatAppClient =
                    await findAnyChatAppClient();


                if (
                    existingChatAppClient
                ) {

                    try {

                        await existingChatAppClient.navigate(
                            chatUrl
                        );

                        await existingChatAppClient.focus();

                        chatTabRegistry.set(
                            roomCode,
                            existingChatAppClient.id
                        );

                        console.log(
                            "Existing ChatApp tab reused."
                        );

                        return;

                    }
                    catch (error) {

                        console.error(
                            "Could not reuse ChatApp tab:",
                            error
                        );
                    }
                }


                // ------------------------------------------------
                // 3. No existing ChatApp tab.
                //
                // Open the exact room directly.
                // ------------------------------------------------

                if (
                    "openWindow"
                    in clients
                ) {

                    try {

                        await clients.openWindow(
                            chatUrl
                        );

                        console.log(
                            "ChatApp opened new notification room."
                        );

                    }
                    catch (error) {

                        console.error(
                            "Could not open ChatApp notification room:",
                            error
                        );
                    }
                }

            })()

                .catch(
                    (error) => {

                        console.error(
                            "ChatApp notification navigation failed:",
                            error
                        );
                    }
                )
        );
    }
);


// ============================================================
// SERVICE WORKER INSTALL
// ============================================================

self.addEventListener(
    "install",
    () => {

        // Activate new service worker immediately.
        self.skipWaiting();
    }
);


// ============================================================
// SERVICE WORKER ACTIVATE
// ============================================================

self.addEventListener(
    "activate",
    (event) => {

        event.waitUntil(

            // Take control of already-open pages.
            clients.claim()

        );
    }
);