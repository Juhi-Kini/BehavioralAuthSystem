
window.docxPreviewInterop = {
    async renderFromBase64(base64, containerId) {
        const container = document.getElementById(containerId);

        if (!container) {
            throw new Error("Word preview container not found.");
        }

        container.replaceChildren();

        try {
            // Check the actual library API loaded by the browser.
            const docxLib = window.docx;

            if (!docxLib || typeof docxLib.renderAsync !== "function") {
                throw new Error(
                    "docx-preview did not load correctly. Check App.razor and the browser Network tab."
                );
            }

            const binary = atob(base64);
            const bytes = new Uint8Array(binary.length);

            for (let i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
            }

            await docxLib.renderAsync(
                bytes.buffer,
                container,
                null,
                {
                    inWrapper: true,
                    breakPages: true,
                    ignoreWidth: false,
                    ignoreHeight: false
                }
            );
        } catch (error) {
            console.error("DOCX preview error:", error);

            const message = document.createElement("pre");
            message.style.cssText =
                "color:#b91c1c;white-space:pre-wrap;padding:20px;";
            message.textContent =
                "Word preview failed:\n" +
                (error?.message || String(error));

            container.replaceChildren(message);
        }
    }
};
