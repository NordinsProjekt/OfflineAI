// Document Analysis Helper Functions

window.documentAnalysis = {
    // Handle file download with proper UTF-8 encoding
    downloadFile: function(fileName, base64Content) {
        try {
            // Decode base64 to binary string
            const binaryString = atob(base64Content);
            
            // Convert to Uint8Array for proper UTF-8 handling
            const bytes = new Uint8Array(binaryString.length);
            for (let i = 0; i < binaryString.length; i++) {
                bytes[i] = binaryString.charCodeAt(i);
            }
            
            // Create blob with UTF-8 charset
            const blob = new Blob([bytes], { type: 'text/plain;charset=utf-8' });
            
            // Create download link
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = fileName;
            
            // Trigger download
            document.body.appendChild(link);
            link.click();
            
            // Cleanup
            setTimeout(() => {
                document.body.removeChild(link);
                URL.revokeObjectURL(url);
            }, 100);
            
            return true;
        } catch (error) {
            console.error('Download error:', error);
            return false;
        }
    },

    // Initialize drag-and-drop for an upload area
    initializeDragDrop: function(uploadAreaId, inputFileId) {
        const uploadArea = document.getElementById(uploadAreaId);
        const inputFile = document.getElementById(inputFileId);
        
        if (!uploadArea || !inputFile) {
            console.warn('Upload area or input file not found', { uploadAreaId, inputFileId });
            return;
        }

        // Prevent default drag behaviors
        ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
            uploadArea.addEventListener(eventName, preventDefaults, false);
            document.body.addEventListener(eventName, preventDefaults, false);
        });

        function preventDefaults(e) {
            e.preventDefault();
            e.stopPropagation();
        }

        // Handle drop
        uploadArea.addEventListener('drop', handleDrop, false);

        function handleDrop(e) {
            const dt = e.dataTransfer;
            const files = dt.files;

            if (files.length > 0) {
                // Create a new DataTransfer object and add the dropped files
                const dataTransfer = new DataTransfer();
                Array.from(files).forEach(file => {
                    dataTransfer.items.add(file);
                });

                // Assign the files to the input element
                inputFile.files = dataTransfer.files;

                // Trigger the change event to notify Blazor
                const event = new Event('change', { bubbles: true });
                inputFile.dispatchEvent(event);
            }
        }
    }
};

// Backward compatibility
window.downloadFile = window.documentAnalysis.downloadFile;
