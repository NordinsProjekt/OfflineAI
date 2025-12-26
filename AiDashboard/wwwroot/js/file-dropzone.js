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
    }
};
