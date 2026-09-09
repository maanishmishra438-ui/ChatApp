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