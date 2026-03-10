// Monaco Editor interop for Blazor

window.monacoInterop = {
    _editor: null,

    init: function (containerId, initialCode) {
        return new Promise(function (resolve) {
            require.config({
                paths: {
                    'vs': 'https://cdn.jsdelivr.net/npm/monaco-editor@0.45.0/min/vs'
                }
            });

            require(['vs/editor/editor.main'], function () {
                // Define a custom dark theme matching the existing XnaFiddle style
                monaco.editor.defineTheme('xnafiddle-dark', {
                    base: 'vs-dark',
                    inherit: true,
                    rules: [],
                    colors: {
                        'editor.background': '#1e1e1e'
                    }
                });

                window.monacoInterop._editor = monaco.editor.create(
                    document.getElementById(containerId),
                    {
                        value: initialCode,
                        language: 'csharp',
                        theme: 'xnafiddle-dark',
                        fontSize: 14,
                        fontFamily: "Consolas, 'Courier New', monospace",
                        lineHeight: 1.5 * 14,
                        minimap: { enabled: false },
                        scrollBeyondLastLine: false,
                        automaticLayout: true,
                        tabSize: 4,
                        renderWhitespace: 'none',
                        overviewRulerLanes: 0,
                        hideCursorInOverviewRuler: true,
                        overviewRulerBorder: false,
                        scrollbar: {
                            verticalScrollbarSize: 10,
                            horizontalScrollbarSize: 10
                        }
                    }
                );

                resolve(true);
            });
        });
    },

    getValue: function () {
        if (window.monacoInterop._editor) {
            return window.monacoInterop._editor.getValue();
        }
        return '';
    },

    setValue: function (code) {
        if (window.monacoInterop._editor) {
            window.monacoInterop._editor.setValue(code);
        }
    },

    setDiagnostics: function (markers) {
        // markers: array of { startLine, startCol, endLine, endCol, message, severity }
        if (window.monacoInterop._editor) {
            var model = window.monacoInterop._editor.getModel();
            var monacoMarkers = markers.map(function (m) {
                return {
                    startLineNumber: m.startLine,
                    startColumn: m.startCol,
                    endLineNumber: m.endLine,
                    endColumn: m.endCol,
                    message: m.message,
                    severity: m.severity === 'error'
                        ? monaco.MarkerSeverity.Error
                        : m.severity === 'warning'
                            ? monaco.MarkerSeverity.Warning
                            : monaco.MarkerSeverity.Info
                };
            });
            monaco.editor.setModelMarkers(model, 'compilation', monacoMarkers);
        }
    },

    clearDiagnostics: function () {
        if (window.monacoInterop._editor) {
            var model = window.monacoInterop._editor.getModel();
            monaco.editor.setModelMarkers(model, 'compilation', []);
        }
    }
};

// Drag-and-drop file upload (images → .NET interop)
window.fileDropInterop = {
    _dotNetRef: null,

    init: function (dotNetRef) {
        this._dotNetRef = dotNetRef;
        var dropTarget = document.getElementById('canvasHolder');
        if (!dropTarget) return;

        dropTarget.addEventListener('dragover', function (e) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
            dropTarget.style.outline = '2px dashed #007acc';
            dropTarget.style.outlineOffset = '-4px';
        });

        dropTarget.addEventListener('dragleave', function (e) {
            dropTarget.style.outline = '';
            dropTarget.style.outlineOffset = '';
        });

        dropTarget.addEventListener('drop', function (e) {
            e.preventDefault();
            dropTarget.style.outline = '';
            dropTarget.style.outlineOffset = '';
            var files = e.dataTransfer.files;
            for (var i = 0; i < files.length; i++) {
                (function (file) {
                    if (!file.type.startsWith('image/')) return;
                    var reader = new FileReader();
                    reader.onload = function () {
                        // Blazor expects byte[] as base64
                        var base64 = reader.result.split(',')[1];
                        window.fileDropInterop._dotNetRef.invokeMethodAsync('OnFileDropped', file.name, base64);
                    };
                    reader.readAsDataURL(file);
                })(files[i]);
            }
        });
    }
};
