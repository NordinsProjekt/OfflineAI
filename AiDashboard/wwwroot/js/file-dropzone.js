// FileDropZone JavaScript utilities
window.fileDropZone = {
    initialize: function (dropZoneElement, inputFileElementId) {
        if (!dropZoneElement) {
            console.error('FileDropZone: Invalid dropZone element provided');
            return;
        }

        const fileInput = document.getElementById(inputFileElementId);
        if (!fileInput) {
            console.error('FileDropZone: Could not find input element with ID:', inputFileElementId);
            return;
        }

        const dropZone = dropZoneElement;

        // Prevent default drag behaviors
        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            dropZone.addEventListener(eventName, preventDefaults, false);
        });

        function preventDefaults(e) {
            e.preventDefault();
            e.stopPropagation();
        }

        // Handle drop event
        dropZone.addEventListener('drop', function (e) {
            const dt = e.dataTransfer;
            const files = dt.files;

            if (files.length > 0) {
                // Create a new FileList-like object that we can assign to the input
                const dataTransfer = new DataTransfer();
                
                // Add the dropped file(s) to the DataTransfer object
                Array.from(files).forEach(file => {
                    dataTransfer.items.add(file);
                });

                // Assign the files to the input element
                fileInput.files = dataTransfer.files;

                // Trigger the change event on the InputFile component
                const event = new Event('change', { bubbles: true });
                fileInput.dispatchEvent(event);
            }
        }, false);
    },

    // Programmatically trigger file selection dialog
    triggerFileSelect: function (inputFileElementId) {
        const fileInput = document.getElementById(inputFileElementId);
        if (fileInput) {
            fileInput.click();
        }
    },

    // Turns an arbitrary container (e.g. the whole chat panel, resolved by CSS selector) into a
    // drop target for AgentFileUpload, feeding dropped files into its hidden <InputFile>. Safe to
    // call on every Blazor render: the actual 'dragenter'/'drop'/etc. listeners are attached only
    // once per drop-zone element (tracked via a data attribute), but the target input id and
    // disabled state are refreshed every call, since AgentFileUpload gives its <InputFile> a new
    // id after each upload (see AgentFileUpload.razor's ResetInput()).
    initializePersistent: function (selector, inputFileElementId, disabled) {
        const dropZone = document.querySelector(selector);
        if (!dropZone) return;

        dropZone.dataset.agentUploadTargetId = inputFileElementId;
        dropZone.dataset.agentUploadDisabled = disabled ? 'true' : 'false';

        if (dropZone.dataset.agentUploadBound === 'true') return;
        dropZone.dataset.agentUploadBound = 'true';

        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            dropZone.addEventListener(eventName, function (e) {
                e.preventDefault();
                e.stopPropagation();
            }, false);
        });

        dropZone.addEventListener('dragenter', function () {
            if (dropZone.dataset.agentUploadDisabled !== 'true') {
                dropZone.classList.add('agent-upload-drag-over');
            }
        }, false);

        dropZone.addEventListener('dragleave', function (e) {
            if (e.target === dropZone) {
                dropZone.classList.remove('agent-upload-drag-over');
            }
        }, false);

        dropZone.addEventListener('drop', function (e) {
            dropZone.classList.remove('agent-upload-drag-over');

            if (dropZone.dataset.agentUploadDisabled === 'true') return;

            const files = e.dataTransfer.files;
            if (files.length === 0) return;

            const fileInput = document.getElementById(dropZone.dataset.agentUploadTargetId);
            if (!fileInput) return;

            const dataTransfer = new DataTransfer();
            Array.from(files).forEach(file => dataTransfer.items.add(file));
            fileInput.files = dataTransfer.files;
            fileInput.dispatchEvent(new Event('change', { bubbles: true }));
        }, false);
    }
};
