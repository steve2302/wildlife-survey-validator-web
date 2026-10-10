// Browser-side helpers called from ValidatorPage.razor.
window.surveyValidator = {

    // Save bytes to the user's machine as a file download.
    downloadFile: async function (fileName, contentType, streamRef) {
        const buffer = await streamRef.arrayBuffer();
        const blob = new Blob([buffer], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        URL.revokeObjectURL(url);
    },

    // Remember the template between visits. Storage can be unavailable (private windows), so never throw.
    getStored: function (key) {
        try { return window.localStorage.getItem(key); } catch (e) { return null; }
    },
    setStored: function (key, value) {
        try { window.localStorage.setItem(key, value); } catch (e) { }
    },
    removeStored: function (key) {
        try { window.localStorage.removeItem(key); } catch (e) { }
    },

    // Ask before closing the tab while there are edits that have not been downloaded.
    setUnsaved: function (unsaved) {
        window.onbeforeunload = unsaved
            ? function (e) { e.preventDefault(); e.returnValue = ""; return ""; }
            : null;
    },

    focus: function (element) {
        if (element) { element.focus(); if (element.select) element.select(); }
    }
};
