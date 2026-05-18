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

                // Register C#/XNA completion + signature help + hover providers
                window.monacoInterop._registerCompletions();
                window.monacoInterop._registerSignatureHelp();
                window.monacoInterop._registerHover();
                window.monacoInterop._registerAddUsingCommand();

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

                window.monacoInterop._editor.onDidChangeModelContent(function () {
                    // Live diagnostics (squiggles) — debounced, readiness-gated.
                    window.monacoInterop._scheduleDiagnostics();

                    if (!window.monacoInterop._changeCallbackRef) return;
                    clearTimeout(window.monacoInterop._changeTimer);
                    window.monacoInterop._changeTimer = setTimeout(function () {
                        window.monacoInterop._changeCallbackRef.invokeMethodAsync('OnEditorContentChanged');
                    }, 500);
                });

                resolve(true);
            });
        });
    },

    _intellisenseRef: null,
    _isIntellisenseReady: false,
    _completionInFlight: false,
    _diagnosticsDebounceMs: 700,
    _diagnosticsTimer: null,

    // Debounced live-diagnostics: re-runs Roslyn GetDiagnosticsAsync a short while
    // after the last keystroke. Uses owner 'roslyn' so markers don't collide with
    // the post-compile path (owner 'compilation'). If a completion request is
    // currently in flight on the single WASM thread, defer briefly to avoid
    // fighting over the thread.
    _scheduleDiagnostics: function () {
        var interop = window.monacoInterop;
        if (!interop._isIntellisenseReady) return;
        if (interop._diagnosticsTimer) {
            clearTimeout(interop._diagnosticsTimer);
            interop._diagnosticsTimer = null;
        }
        var fire = function () {
            interop._diagnosticsTimer = null;
            if (interop._completionInFlight) {
                interop._diagnosticsTimer = setTimeout(fire, 300);
                return;
            }
            interop._runDiagnostics();
        };
        interop._diagnosticsTimer = setTimeout(fire, interop._diagnosticsDebounceMs);
    },

    _runDiagnostics: async function () {
        var interop = window.monacoInterop;
        var ref = interop._intellisenseRef;
        if (!ref || !interop._editor) return;
        var model = interop._editor.getModel();
        if (!model) return;
        var source = model.getValue();
        var diags;
        try {
            diags = await ref.invokeMethodAsync('GetDiagnosticsAsync', source);
        } catch (e) {
            console.warn('Live diagnostics failed:', e);
            return;
        }
        if (!diags) diags = [];
        var markers = diags.map(function (d) {
            var startPos = model.getPositionAt(d.startOffset);
            var endPos = model.getPositionAt(d.endOffset);
            var severity = d.severity === 'error'
                ? monaco.MarkerSeverity.Error
                : d.severity === 'warning'
                    ? monaco.MarkerSeverity.Warning
                    : monaco.MarkerSeverity.Info;
            return {
                startLineNumber: startPos.lineNumber,
                startColumn: startPos.column,
                endLineNumber: endPos.lineNumber,
                endColumn: endPos.column,
                message: d.message,
                severity: severity,
                code: d.code
            };
        });
        monaco.editor.setModelMarkers(model, 'roslyn', markers);
    },

    setIntellisenseRef: function (dotNetRef) {
        window.monacoInterop._intellisenseRef = dotNetRef;
    },

    // Called from Blazor once IntellisenseService.WarmupAsync completes. Until this is
    // true, the completion provider skips .NET calls entirely — we don't want to queue
    // keystroke-driven requests behind the ~5s first-run warmup on the single WASM thread.
    setIntellisenseReady: function (ready) {
        window.monacoInterop._isIntellisenseReady = !!ready;
    },

    // Map a Roslyn completion tag (item.Tags[0]) to a Monaco CompletionItemKind.
    // Roslyn tag strings come from Microsoft.CodeAnalysis.Tags.WellKnownTags.
    _mapKind: function (tag) {
        var Kind = monaco.languages.CompletionItemKind;
        switch (tag) {
            case 'Class':     return Kind.Class;
            case 'Struct':    return Kind.Struct;
            case 'Interface': return Kind.Interface;
            case 'Enum':      return Kind.Enum;
            case 'EnumMember':return Kind.EnumMember;
            case 'Delegate':  return Kind.Interface;
            case 'Method':    return Kind.Method;
            case 'ExtensionMethod': return Kind.Method;
            case 'Property':  return Kind.Property;
            case 'Field':     return Kind.Field;
            case 'Constant':  return Kind.Constant;
            case 'Event':     return Kind.Event;
            case 'Local':     return Kind.Variable;
            case 'Parameter': return Kind.Variable;
            case 'RangeVariable': return Kind.Variable;
            case 'Keyword':   return Kind.Keyword;
            case 'Namespace': return Kind.Module;
            case 'Module':    return Kind.Module;
            case 'Label':     return Kind.Text;
            case 'Snippet':   return Kind.Snippet;
            case 'TypeParameter': return Kind.TypeParameter;
            case 'Operator':  return Kind.Operator;
            default:          return Kind.Text;
        }
    },

    _completionDebounceTimer: null,
    _completionAbortResolve: null,
    // Debounce values. Note: Monaco reports BOTH explicit Ctrl+Space AND auto-trigger-while-typing
    // as triggerKind=0 (Invoke) — there is no separate "typing" kind. So we must NOT branch on
    // triggerKind=Invoke to pick a fast path; instead we branch on what character was just typed
    // (triggerCharacter === '.') vs. everything else (which falls through to the word-based rules).
    _completionDebounceTriggerCharMs: 50,
    _completionDebounceTypingMs: 400,
    // Ctrl+Space with an empty word ("show me everything in scope") gets its own immediate path.
    _completionDebounceForceMs: 0,
    _completionMinAutoTriggerLen: 3,
    // Client-side cache keyed by {sourcePrefixUpToWordStart, wordStartOffset}.
    // Monaco filters suggestions client-side as the user types, so as long as the
    // prefix up to the current word is unchanged we can reuse the last .NET result
    // and avoid blocking the single WASM thread on every keystroke.
    _completionCache: null,

    _registerCompletions: function () {
        monaco.languages.registerCompletionItemProvider('csharp', {
            triggerCharacters: ['.'],
            provideCompletionItems: function (model, position, context, token) {
                var interop = window.monacoInterop;

                // Compute cache key. If word-start context matches the last cached call,
                // return cached suggestions synchronously — no .NET call, no debounce.
                var wordInfo = model.getWordUntilPosition(position);
                var wordStartOffset = model.getOffsetAt({
                    lineNumber: position.lineNumber,
                    column: wordInfo.startColumn
                });
                var sourcePrefix = model.getValue().substring(0, wordStartOffset);
                var cached = interop._completionCache;
                var buildCachedResult = function () {
                    var cachedRange = {
                        startLineNumber: position.lineNumber,
                        startColumn: wordInfo.startColumn,
                        endLineNumber: position.lineNumber,
                        endColumn: wordInfo.endColumn
                    };
                    // Rebind range to current cursor position; suggestions are otherwise identical.
                    var cachedSuggestions = cached.suggestions.map(function (s) {
                        return {
                            label: s.label,
                            insertText: s.insertText,
                            kind: s.kind,
                            detail: s.detail,
                            sortText: s.sortText,
                            range: cachedRange
                        };
                    });
                    return { suggestions: cachedSuggestions, incomplete: false };
                };
                var cacheHit = cached
                    && cached.wordStartOffset === wordStartOffset
                    && cached.sourcePrefix === sourcePrefix;
                if (cacheHit) {
                    return buildCachedResult();
                }

                // Monaco's CompletionTriggerKind: 0 Invoke, 1 TriggerCharacter, 2 TriggerForIncompleteCompletions.
                // IMPORTANT: Monaco reports BOTH explicit Ctrl+Space AND auto-trigger-from-typing
                // as triggerKind=Invoke (0). There is no separate "typing" kind. So we branch on
                // what character was just typed (triggerCharacter) rather than triggerKind alone.
                // `context` may be undefined in rare edge cases; guard accordingly.
                var triggerKind = context && typeof context.triggerKind === 'number'
                    ? context.triggerKind
                    : undefined;
                var triggerCharacter = context ? context.triggerCharacter : undefined;
                var Kinds = monaco.languages.CompletionTriggerKind || {};
                // Prefer the enum values from monaco; fall back to the documented numerics.
                var invokeKind = typeof Kinds.Invoke === 'number' ? Kinds.Invoke : 0;
                var triggerCharKind = typeof Kinds.TriggerCharacter === 'number' ? Kinds.TriggerCharacter : 1;
                var incompleteKind = typeof Kinds.TriggerForIncompleteCompletions === 'number'
                    ? Kinds.TriggerForIncompleteCompletions
                    : 2;

                // TriggerForIncompleteCompletions: Monaco re-asks when the last result was
                // flagged incomplete. Never call .NET here — return cached if available, else empty.
                if (triggerKind === incompleteKind) {
                    if (cached) {
                        cacheHit = true;
                        return buildCachedResult();
                    }
                    return { suggestions: [] };
                }

                // Readiness gate: if Roslyn warmup hasn't finished, skip .NET entirely
                // rather than queueing completion work behind the single-threaded warmup.
                if (!interop._isIntellisenseReady) {
                    return { suggestions: [] };
                }

                // Decide debounce by looking at the just-typed character, not just triggerKind.
                var debounceMs;
                var currentWordLen = wordInfo.word ? wordInfo.word.length : 0;
                if (triggerKind === triggerCharKind || triggerCharacter === '.') {
                    // Member-access path: snappy.
                    debounceMs = interop._completionDebounceTriggerCharMs;
                } else if (triggerKind === invokeKind && currentWordLen === 0) {
                    // Ctrl+Space on empty word — user explicitly asked for "show me everything in scope".
                    // Fire .NET immediately. (Cursor-is-inside-a-word Ctrl+Space falls through to the
                    // word-length gate below, which is deliberate per the debounce design.)
                    debounceMs = interop._completionDebounceForceMs;
                } else {
                    // Typing path (also covers Ctrl+Space inside a partial word).
                    if (currentWordLen < interop._completionMinAutoTriggerLen) {
                        return { suggestions: [] };
                    }
                    debounceMs = interop._completionDebounceTypingMs;
                }

                // Resolve the prior pending request with empty results so Monaco doesn't hang.
                if (interop._completionAbortResolve) {
                    var priorResolve = interop._completionAbortResolve;
                    interop._completionAbortResolve = null;
                    priorResolve({ suggestions: [] });
                }
                if (interop._completionDebounceTimer) {
                    clearTimeout(interop._completionDebounceTimer);
                    interop._completionDebounceTimer = null;
                }

                return new Promise(function (resolve) {
                    interop._completionAbortResolve = resolve;

                    var cancelSub = token.onCancellationRequested(function () {
                        if (interop._completionDebounceTimer) {
                            clearTimeout(interop._completionDebounceTimer);
                            interop._completionDebounceTimer = null;
                        }
                        if (interop._completionAbortResolve === resolve) {
                            interop._completionAbortResolve = null;
                            resolve({ suggestions: [] });
                        }
                    });

                    interop._completionDebounceTimer = setTimeout(async function () {
                        interop._completionDebounceTimer = null;

                        if (token.isCancellationRequested) {
                            if (interop._completionAbortResolve === resolve) {
                                interop._completionAbortResolve = null;
                            }
                            resolve({ suggestions: [] });
                            return;
                        }

                        var ref = interop._intellisenseRef;
                        if (!ref) {
                            if (interop._completionAbortResolve === resolve) {
                                interop._completionAbortResolve = null;
                            }
                            resolve({ suggestions: [] });
                            return;
                        }

                        var word = model.getWordUntilPosition(position);
                        var range = {
                            startLineNumber: position.lineNumber,
                            startColumn: word.startColumn,
                            endLineNumber: position.lineNumber,
                            endColumn: word.endColumn
                        };

                        var source = model.getValue();
                        var offset = model.getOffsetAt(position);

                        var items;
                        interop._completionInFlight = true;
                        try {
                            items = await ref.invokeMethodAsync('GetCompletionsAsync', source, offset);
                        } catch (e) {
                            interop._completionInFlight = false;
                            console.warn('IntelliSense failed:', e);
                            if (interop._completionAbortResolve === resolve) {
                                interop._completionAbortResolve = null;
                            }
                            resolve({ suggestions: [] });
                            return;
                        }

                        interop._completionInFlight = false;
                        if (interop._completionAbortResolve === resolve) {
                            interop._completionAbortResolve = null;
                        }

                        if (token.isCancellationRequested || !items) {
                            resolve({ suggestions: [] });
                            return;
                        }

                        var suggestions = items.map(function (it) {
                            return {
                                label: it.displayText,
                                insertText: it.insertionText,
                                kind: interop._mapKind(it.kind),
                                detail: it.detail,
                                sortText: it.sortText,
                                range: range
                            };
                        });
                        // Cache for subsequent keystrokes within the same word.
                        interop._completionCache = {
                            wordStartOffset: wordStartOffset,
                            sourcePrefix: sourcePrefix,
                            suggestions: suggestions
                        };
                        resolve({ suggestions: suggestions, incomplete: false });
                    }, debounceMs);
                });
            }
        });
    },


    _registerSignatureHelp: function () {
        monaco.languages.registerSignatureHelpProvider('csharp', {
            signatureHelpTriggerCharacters: ['(', ','],
            signatureHelpRetriggerCharacters: [','],
            provideSignatureHelp: async function (model, position, token, context) {
                var interop = window.monacoInterop;
                var ref = interop._intellisenseRef;
                if (!ref) return null;

                var source = model.getValue();
                var offset = model.getOffsetAt(position);

                var result;
                try {
                    result = await ref.invokeMethodAsync('GetSignatureHelpAsync', source, offset);
                } catch (e) {
                    console.warn('Signature help failed:', e);
                    return null;
                }

                if (!result || !result.signatures || result.signatures.length === 0) return null;

                var signatures = result.signatures.map(function (sig) {
                    return {
                        label: sig.label,
                        documentation: sig.documentation || '',
                        parameters: (sig.parameters || []).map(function (p) {
                            // Use tuple form [startOffset, endOffset] so Monaco can
                            // unambiguously highlight the active parameter even when
                            // multiple parameters share the same type name.
                            return {
                                label: [p.startOffset, p.endOffset],
                                documentation: p.documentation || ''
                            };
                        })
                    };
                });

                return {
                    value: {
                        signatures: signatures,
                        activeSignature: result.activeSignature || 0,
                        activeParameter: result.activeParameter || 0
                    },
                    dispose: function () {}
                };
            }
        });
    },

    _registerHover: function () {
        monaco.languages.registerHoverProvider('csharp', {
            provideHover: async function (model, position, token) {
                var interop = window.monacoInterop;
                var ref = interop._intellisenseRef;
                if (!ref) return undefined;
                // Readiness gate: before Roslyn warmup finishes, skip .NET to avoid
                // queueing hover work behind the single-threaded warmup.
                if (!interop._isIntellisenseReady) return undefined;

                var source = model.getValue();
                var offset = model.getOffsetAt(position);

                var result;
                try {
                    result = await ref.invokeMethodAsync('GetHoverAsync', source, offset);
                } catch (e) {
                    console.warn('Hover failed:', e);
                    return undefined;
                }

                if (!result || token.isCancellationRequested) return undefined;

                var startPos = model.getPositionAt(result.startOffset);
                var endPos = model.getPositionAt(result.endOffset);
                // isTrusted: true is REQUIRED for Monaco to honor `command:` URIs in
                // markdown links. Without it the "Add using ...;" links produced by
                // IntellisenseService.BuildAddUsingMarkdown would render as plain,
                // non-clickable text (Monaco strips untrusted command links silently).
                return {
                    range: new monaco.Range(
                        startPos.lineNumber, startPos.column,
                        endPos.lineNumber, endPos.column),
                    contents: [{ value: result.content, isTrusted: true, supportHtml: false }]
                };
            }
        });
    },

    // Registers the `xnafiddle.addUsing` command invoked by `command:` URIs in the
    // hover-tooltip markdown produced by IntellisenseService.BuildAddUsingMarkdown.
    // The original Monaco code-action-provider path was abandoned: in this CDN-loaded
    // standalone build the action-widget's ListWidget virtualization allocates height
    // but renders zero visible rows, so users never see the quick-fix items. The
    // hover-tooltip path below works reliably and reuses this command for the actual
    // edit application.
    //
    // Command signature (from BuildAddUsingMarkdown): [modelUri, insertOffset, insertText].
    // The .NET side doesn't know the model URI, so it passes the sentinel
    // '__ACTIVE_MODEL__' and we resolve to the current editor's model here.
    _registerAddUsingCommand: function () {
        try {
            if (!monaco.editor || typeof monaco.editor.registerCommand !== 'function') {
                console.warn('[AddUsing] monaco.editor.registerCommand not available; "Add using" hover links will be inert');
                return;
            }
            monaco.editor.registerCommand('xnafiddle.addUsing', function (_accessor, uriRaw, insertOffset, insertText) {
                try {
                    var model = null;
                    if (uriRaw === '__ACTIVE_MODEL__') {
                        var editor = window.monacoInterop._editor
                            || (monaco.editor.getEditors && monaco.editor.getEditors()[0]);
                        model = editor ? editor.getModel() : null;
                    } else {
                        var uri = (uriRaw && uriRaw.scheme) ? uriRaw : monaco.Uri.parse(String(uriRaw));
                        model = monaco.editor.getModel(uri);
                    }
                    if (!model) return;
                    var pos = model.getPositionAt(insertOffset);
                    model.pushEditOperations(
                        [],
                        [{
                            range: {
                                startLineNumber: pos.lineNumber,
                                startColumn: pos.column,
                                endLineNumber: pos.lineNumber,
                                endColumn: pos.column
                            },
                            text: insertText,
                            forceMoveMarkers: true
                        }],
                        function () { return null; }
                    );

                    // Dismiss the hover tooltip: focus the editor and fire an
                    // Escape keydown, which Monaco treats as the canonical
                    // close-all-floating-widgets signal.
                    //
                    // FRAGILE: this is the only sequence that reliably dismisses
                    // the hover in Monaco 0.45 (CDN) after a command-link click.
                    // Things that do NOT work and should NOT be reintroduced:
                    //   - hover contribution methods (hideContentHover / hide) — no-op in this build
                    //   - ed.trigger('source', 'closeHover', {}) — no matching action
                    //   - setting display:none on .monaco-hover — kills all FUTURE hovers
                    //     because Monaco reuses the same DOM node
                    //   - ed.focus() alone — doesn't dismiss when hover is in sticky-interaction mode
                    // See .claude/skills/intellisense/SKILL.md, "Hover dismiss after click".
                    try {
                        var ed = window.monacoInterop._editor
                            || (monaco.editor.getEditors && monaco.editor.getEditors()[0]);
                        if (ed) {
                            if (typeof ed.focus === 'function') ed.focus();
                            var domNode = ed.getDomNode && ed.getDomNode();
                            var target = (domNode && domNode.querySelector && domNode.querySelector('textarea'))
                                || domNode;
                            if (target && typeof target.dispatchEvent === 'function') {
                                target.dispatchEvent(new KeyboardEvent('keydown', {
                                    key: 'Escape',
                                    code: 'Escape',
                                    keyCode: 27,
                                    which: 27,
                                    bubbles: true,
                                    cancelable: true
                                }));
                            }
                        }
                    } catch (_) { /* best-effort dismiss */ }
                } catch (e) {
                    console.warn('xnafiddle.addUsing failed:', e);
                }
            });
        } catch (e) {
            console.warn('[AddUsing] registerCommand threw:', e);
        }
    },

    registerChangeCallback: function (dotNetRef) {
        window.monacoInterop._changeCallbackRef = dotNetRef;
        window.monacoInterop._changeTimer = null;
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

// Compile timer — updates #compileTimer directly so it runs during synchronous .NET work
window.compileTimerInterop = {
    _interval: null,
    _startTime: null,

    start: function () {
        this._startTime = Date.now();
        clearInterval(this._interval);
        this._interval = setInterval(function () {
            var el = document.getElementById('compileTimer');
            if (el) {
                var secs = (Date.now() - window.compileTimerInterop._startTime) / 1000;
                el.textContent = secs.toFixed(1) + 's';
            }
        }, 100);
    },

    stop: function () {
        clearInterval(this._interval);
        this._interval = null;
    }
};

// Prevent KNI from receiving keyboard events when a text input (Monaco editor,
// the export filename box, etc.) has focus.
//
// Why not gate on canvas focus instead (the usual pattern)? KNI registers its
// keydown/keyup on `window`, not on the canvas — the canvas never sees the
// events as a listener target, so there's nothing to gate there without
// patching KNI itself. We intercept on `document` in the bubble phase and stop
// propagation before it reaches `window`. A capture-phase listener on `window`
// was tried first but fires before Monaco's own handlers, so Monaco never saw
// the keys. Bubbling on `document` lets Monaco (and any other input) handle
// first, then we cut off the event before KNI sees it.
//
// Consequence: every new text-input-like element must be recognized by
// isTextInputFocused below, or its keystrokes will leak into the running game.
function isTextInputFocused() {
    var active = document.activeElement;
    if (!active) return false;
    if (active.closest('.monaco-editor')) return true;
    var tag = active.tagName;
    if (tag === 'TEXTAREA') return true;
    if (tag === 'INPUT') {
        var type = (active.getAttribute('type') || 'text').toLowerCase();
        // Treat text-like input types as capturing keyboard.
        return type === 'text' || type === 'search' || type === 'url' ||
               type === 'email' || type === 'password' || type === 'tel' ||
               type === 'number';
    }
    if (active.isContentEditable) return true;
    return false;
}
(function () {
    document.addEventListener('keydown', function (e) {
        if (isTextInputFocused() && e.key !== 'F5') e.stopPropagation();
    });
    document.addEventListener('keyup', function (e) {
        if (isTextInputFocused()) e.stopPropagation();
    });
})();

// Keyboard shortcuts interop (e.g. F5 → compile & run)
window.keyboardInterop = {
    init: function (dotNetRef) {
        document.addEventListener('keydown', function (e) {
            if (e.key === 'F5') {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('TriggerCompileAndRun');
            }
            // Ctrl+S / Cmd+S: re-route the browser's "Save Page" shortcut to
            // compile-and-run, matching user muscle memory in a live editor.
            if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) {
                e.preventDefault();
                dotNetRef.invokeMethodAsync('TriggerCompileAndRun');
            }
            // Suppress Tab and arrow keys unless a text input is focused,
            // preventing the browser from cycling focus or scrolling the page
            // while the game canvas is active. Any text-input-like element
            // (Monaco, <input type=text>, etc.) needs arrows/Tab for caret
            // movement and field navigation, so let those through.
            if (e.key === 'Tab' || e.key === 'ArrowLeft' || e.key === 'ArrowRight' || e.key === 'ArrowUp' || e.key === 'ArrowDown') {
                if (!isTextInputFocused()) e.preventDefault();
            }
        });
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
                    var reader = new FileReader();
                    reader.onload = function () {
                        var base64 = reader.result.split(',')[1];
                        window.fileDropInterop._dotNetRef.invokeMethodAsync('OnFileDropped', file.name, base64);
                    };
                    reader.readAsDataURL(file);
                })(files[i]);
            }
        });
    }
};
