window.chatApp = {

    observer: null,

    dotNetHelper: null,


    // ============================================================
    // OBSERVE MESSAGES
    // ============================================================

    observeMessages: function (dotNetHelper) {

        if (!dotNetHelper) {
            return;
        }

        this.dotNetHelper = dotNetHelper;


        // --------------------------------------------------------
        // Create only one IntersectionObserver
        // --------------------------------------------------------

        if (!this.observer) {

            this.observer =
                new IntersectionObserver(
                    entries => {

                        entries.forEach(entry => {

                            if (!entry.isIntersecting) {
                                return;
                            }


                            const element =
                                entry.target;


                            // ------------------------------------------------
                            // Only incoming messages
                            // ------------------------------------------------

                            if (
                                element.classList.contains("mine")
                            ) {
                                return;
                            }


                            // ------------------------------------------------
                            // Already read
                            // ------------------------------------------------

                            const isRead =
                                element.getAttribute(
                                    "data-is-read"
                                ) === "true";

                            if (isRead) {
                                return;
                            }


                            // ------------------------------------------------
                            // Message ID
                            // ------------------------------------------------

                            const messageId =
                                element.getAttribute(
                                    "data-message-id"
                                );

                            if (!messageId) {
                                return;
                            }


                            // ------------------------------------------------
                            // Prevent duplicate calls
                            // ------------------------------------------------

                            element.setAttribute(
                                "data-is-read",
                                "true"
                            );


                            // ------------------------------------------------
                            // Call Blazor
                            // ------------------------------------------------

                            if (this.dotNetHelper) {

                                this.dotNetHelper
                                    .invokeMethodAsync(
                                        "MarkVisibleMessage",
                                        Number(messageId)
                                    )
                                    .catch(() => {
                                    });

                            }

                        });

                    },
                    {
                        threshold: 0.60
                    }
                );
        }


        // --------------------------------------------------------
        // Observe new incoming messages
        // --------------------------------------------------------

        document
            .querySelectorAll(
                ".message-row.theirs[data-message-id]"
            )
            .forEach(element => {

                if (
                    element.getAttribute(
                        "data-observed"
                    ) === "true"
                ) {
                    return;
                }


                element.setAttribute(
                    "data-observed",
                    "true"
                );


                this.observer.observe(element);

            });
    },


    // ============================================================
    // FOCUS MESSAGE INPUT
    // ============================================================

    focusMessageInput: function () {

        const input =
            document.querySelector(
                ".composer textarea"
            );


        if (!input) {
            return;
        }


        input.focus();


        input.setSelectionRange(
            input.value.length,
            input.value.length
        );
    },


    // ============================================================
    // SCROLL TO BOTTOM
    // ============================================================

    scrollToBottom: function () {

        const messages =
            document.querySelector(
                ".messages"
            );


        if (!messages) {
            return;
        }


        messages.scrollTo({
            top: messages.scrollHeight,
            behavior: "smooth"
        });
    },


    // ============================================================
    // INITIALIZE
    // ============================================================

    initialize: function () {
        // Reserved for future chat initialization.
    }

};

// ============================================================
// VIEW ONCE PHOTO
// ============================================================

window.fetchViewOncePhoto = async function (messageId, roomCode, userName) {
    try {
        if (!messageId || !roomCode || !userName) {
            throw new Error("Invalid View Once photo request.");
        }

        const response = await fetch(
            `/api/media/view-once/${encodeURIComponent(messageId)}`,
            {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    roomCode: roomCode,
                    userName: userName
                })
            }
        );

        if (!response.ok) {
            if (response.status === 410) {
                throw new Error(
                    "This view-once photo has already been opened or is no longer available."
                );
            }

            if (response.status === 403) {
                throw new Error(
                    "You cannot open this photo."
                );
            }

            let message = "Unable to open photo.";

            try {
                const data = await response.json();

                if (data && data.message) {
                    message = data.message;
                }
            }
            catch {
                // Ignore JSON parsing failure.
            }

            throw new Error(message);
        }

        const blob = await response.blob();

        if (!blob || blob.size === 0) {
            throw new Error("Photo is empty or unavailable.");
        }

        return URL.createObjectURL(blob);
    }
    catch (error) {
        console.error("View Once photo error:", error);
        throw error;
    }
};