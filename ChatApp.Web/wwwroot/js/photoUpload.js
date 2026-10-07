export function createPreview(inputId) {
    const input = document.getElementById(inputId);

    if (!input || !input.files || input.files.length === 0) {
        return "";
    }

    const file = input.files[0];

    if (!file || !file.type || !file.type.startsWith("image/")) {
        return "";
    }

    const oldPreviewUrl = input.dataset.previewUrl;

    if (oldPreviewUrl) {
        try {
            URL.revokeObjectURL(oldPreviewUrl);
        } catch {
        }
    }

    const previewUrl = URL.createObjectURL(file);

    input.dataset.previewUrl = previewUrl;

    return previewUrl;
}

export function clearPreview(inputId) {
    const input = document.getElementById(inputId);

    if (!input) {
        return;
    }

    const previewUrl = input.dataset.previewUrl;

    if (previewUrl) {
        try {
            URL.revokeObjectURL(previewUrl);
        } catch {
        }

        delete input.dataset.previewUrl;
    }
}