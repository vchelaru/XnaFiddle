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

                // Register C#/XNA completion provider
                window.monacoInterop._registerCompletions();

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

    _registerCompletions: function () {
        var Kind = monaco.languages.CompletionItemKind;
        var Insert = monaco.languages.CompletionItemInsertTextRule.InsertAsSnippet;

        // --- Static completion items ---

        var csharpKeywords = [
            'abstract', 'as', 'async', 'await', 'base', 'bool', 'break', 'byte',
            'case', 'catch', 'char', 'class', 'const', 'continue', 'decimal',
            'default', 'delegate', 'do', 'double', 'else', 'enum', 'event',
            'false', 'finally', 'float', 'for', 'foreach', 'if', 'in', 'int',
            'interface', 'internal', 'is', 'lock', 'long', 'namespace', 'new',
            'null', 'object', 'out', 'override', 'params', 'private', 'protected',
            'public', 'readonly', 'ref', 'return', 'sealed', 'short', 'sizeof',
            'static', 'string', 'struct', 'switch', 'this', 'throw', 'true',
            'try', 'typeof', 'uint', 'ulong', 'unsafe', 'ushort', 'using', 'var',
            'virtual', 'void', 'volatile', 'while'
        ];

        // XNA/KNI types with their members
        var xnaTypes = {
            'Game':              { kind: Kind.Class, members: ['Initialize', 'LoadContent', 'UnloadContent', 'Update', 'Draw', 'Content', 'GraphicsDevice', 'Window', 'IsMouseVisible', 'Run', 'Exit', 'Services', 'Tick', 'Dispose'] },
            'GameTime':          { kind: Kind.Class, members: ['ElapsedGameTime', 'TotalGameTime', 'IsRunningSlowly'] },
            'GraphicsDeviceManager': { kind: Kind.Class, members: ['PreferredBackBufferWidth', 'PreferredBackBufferHeight', 'IsFullScreen', 'GraphicsProfile', 'ApplyChanges', 'GraphicsDevice'] },
            'GraphicsDevice':    { kind: Kind.Class, members: ['Clear', 'Viewport', 'PresentationParameters', 'SetRenderTarget', 'DrawPrimitives', 'DrawIndexedPrimitives'] },
            'SpriteBatch':       { kind: Kind.Class, members: ['Begin', 'End', 'Draw', 'DrawString'] },
            'Texture2D':         { kind: Kind.Class, members: ['Width', 'Height', 'SetData', 'GetData', 'Bounds'] },
            'SpriteFont':        { kind: Kind.Class, members: ['MeasureString', 'LineSpacing', 'Spacing'] },
            'Color':             { kind: Kind.Struct, members: ['R', 'G', 'B', 'A', 'White', 'Black', 'Red', 'Green', 'Blue', 'Yellow', 'Cyan', 'Magenta', 'Orange', 'Purple', 'Gray', 'DarkGray', 'LightGray', 'CornflowerBlue', 'Transparent', 'TransparentBlack', 'Coral', 'Lerp', 'Multiply'] },
            'Vector2':           { kind: Kind.Struct, members: ['X', 'Y', 'Zero', 'One', 'UnitX', 'UnitY', 'Length', 'LengthSquared', 'Normalize', 'Distance', 'DistanceSquared', 'Dot', 'Lerp', 'Clamp', 'Min', 'Max', 'Transform', 'Negate'] },
            'Vector3':           { kind: Kind.Struct, members: ['X', 'Y', 'Z', 'Zero', 'One', 'Forward', 'Backward', 'Up', 'Down', 'Left', 'Right', 'UnitX', 'UnitY', 'UnitZ', 'Length', 'Normalize', 'Cross', 'Dot', 'Lerp', 'Distance', 'Transform'] },
            'Vector4':           { kind: Kind.Struct, members: ['X', 'Y', 'Z', 'W', 'Zero', 'One', 'Lerp', 'Transform'] },
            'Matrix':            { kind: Kind.Struct, members: ['Identity', 'CreateTranslation', 'CreateRotationX', 'CreateRotationY', 'CreateRotationZ', 'CreateScale', 'CreateOrthographic', 'CreateOrthographicOffCenter', 'CreatePerspective', 'CreatePerspectiveFieldOfView', 'CreateLookAt', 'CreateWorld', 'Invert', 'Transpose', 'Lerp', 'Multiply'] },
            'Rectangle':         { kind: Kind.Struct, members: ['X', 'Y', 'Width', 'Height', 'Left', 'Right', 'Top', 'Bottom', 'Center', 'Location', 'Size', 'Contains', 'Intersects', 'Inflate', 'Offset', 'Union', 'Intersect', 'Empty'] },
            'Point':             { kind: Kind.Struct, members: ['X', 'Y', 'Zero'] },
            'MathHelper':        { kind: Kind.Class, members: ['Pi', 'TwoPi', 'PiOver2', 'PiOver4', 'E', 'ToRadians', 'ToDegrees', 'Lerp', 'Clamp', 'Min', 'Max', 'SmoothStep', 'Distance', 'WrapAngle'] },
            'Viewport':          { kind: Kind.Struct, members: ['X', 'Y', 'Width', 'Height', 'Bounds'] },
            'ContentManager':    { kind: Kind.Class, members: ['Load', 'Unload', 'RootDirectory'] },
            'Mouse':             { kind: Kind.Class, members: ['GetState'] },
            'MouseState':        { kind: Kind.Struct, members: ['X', 'Y', 'LeftButton', 'RightButton', 'MiddleButton', 'ScrollWheelValue'] },
            'Keyboard':          { kind: Kind.Class, members: ['GetState'] },
            'KeyboardState':     { kind: Kind.Struct, members: ['IsKeyDown', 'IsKeyUp', 'GetPressedKeys'] },
            'Keys':              { kind: Kind.Enum, members: ['A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'Space', 'Enter', 'Escape', 'Tab', 'Back', 'Delete', 'Left', 'Right', 'Up', 'Down', 'LeftShift', 'RightShift', 'LeftControl', 'RightControl'] },
            'ButtonState':       { kind: Kind.Enum, members: ['Pressed', 'Released'] },
            'GraphicsProfile':   { kind: Kind.Enum, members: ['Reach', 'HiDef'] },
            'SpriteSortMode':    { kind: Kind.Enum, members: ['Deferred', 'Immediate', 'Texture', 'BackToFront', 'FrontToBack'] },
            'BlendState':        { kind: Kind.Class, members: ['AlphaBlend', 'Additive', 'NonPremultiplied', 'Opaque'] },
            'SamplerState':      { kind: Kind.Class, members: ['PointClamp', 'PointWrap', 'LinearClamp', 'LinearWrap', 'AnisotropicClamp', 'AnisotropicWrap'] },
            'RenderTarget2D':    { kind: Kind.Class, members: ['Width', 'Height', 'Bounds'] },
            'BasicEffect':       { kind: Kind.Class, members: ['World', 'View', 'Projection', 'DiffuseColor', 'Alpha', 'VertexColorEnabled', 'TextureEnabled', 'Texture', 'LightingEnabled', 'CurrentTechnique'] },
            'Effect':            { kind: Kind.Class, members: ['Parameters', 'CurrentTechnique', 'Techniques'] },
            'TimeSpan':          { kind: Kind.Struct, members: ['TotalSeconds', 'TotalMilliseconds', 'Seconds', 'Milliseconds', 'Zero', 'FromSeconds', 'FromMilliseconds'] },
            // Gum UI
            'GumService':        { kind: Kind.Class, members: ['Default', 'Initialize', 'Update', 'Draw'] },
            'DefaultVisualsVersion': { kind: Kind.Enum, members: ['V1', 'V2', 'V3'] },
            'StackPanel':        { kind: Kind.Class, members: ['Spacing', 'AddToRoot', 'AddChild', 'Children', 'Width', 'Height'] },
            'Label':             { kind: Kind.Class, members: ['Text', 'Width', 'Height'] },
            'Button':            { kind: Kind.Class, members: ['Text', 'Width', 'Height', 'Click'] },
            'TextBox':           { kind: Kind.Class, members: ['Text', 'Placeholder', 'Width', 'Height', 'TextChanged'] },
            'CheckBox':          { kind: Kind.Class, members: ['Text', 'IsChecked', 'Checked', 'Unchecked'] },
            'Slider':            { kind: Kind.Class, members: ['Value', 'Minimum', 'Maximum', 'Width', 'ValueChanged'] },
            'ComboBox':          { kind: Kind.Class, members: ['Items', 'SelectedObject', 'SelectedIndex', 'SelectionChanged'] },
            'ListBox':           { kind: Kind.Class, members: ['Items', 'Visual', 'SelectedObject', 'SelectedIndex', 'SelectionChanged'] },
            'RadioButton':       { kind: Kind.Class, members: ['Text', 'IsChecked', 'Checked'] },
            // Apos.Shapes
            'ShapeBatch':        { kind: Kind.Class, members: ['Begin', 'End', 'DrawCircle', 'FillCircle', 'DrawRectangle', 'FillRectangle', 'BorderCircle', 'BorderRectangle', 'BorderLine', 'DrawLine', 'FillLine'] },
        };

        // Known namespaces (for 'using' directive completions)
        var knownNamespaces = [
            // XNA/KNI
            'Microsoft.Xna.Framework',
            'Microsoft.Xna.Framework.Graphics',
            'Microsoft.Xna.Framework.Input',
            'Microsoft.Xna.Framework.Input.Touch',
            'Microsoft.Xna.Framework.Audio',
            'Microsoft.Xna.Framework.Content',
            'Microsoft.Xna.Framework.Media',
            // MonoGameGum / Gum
            'MonoGameGum',
            'Gum.Forms',
            'Gum.Forms.Controls',
            'Gum.DataTypes',
            // Apos.Shapes
            'Apos.Shapes',
            // MonoGame.Extended
            'MonoGame.Extended',
            'MonoGame.Extended.Sprites',
            'MonoGame.Extended.Content',
            'MonoGame.Extended.Input',
            'MonoGame.Extended.Tiled',
            // System
            'System',
            'System.Collections.Generic',
            'System.Collections',
            'System.Linq',
            'System.Text',
            'System.IO',
            'System.Numerics',
            'System.Threading.Tasks',
            'System.Threading',
        ];

        // Snippet templates
        var snippets = [
            { label: 'game class', detail: 'XNA Game class template', text: 'public class ${1:MyGame} : Game\n{\n\tGraphicsDeviceManager _graphics;\n\tSpriteBatch _spriteBatch;\n\n\tpublic ${1:MyGame}()\n\t{\n\t\t_graphics = new GraphicsDeviceManager(this);\n\t\tIsMouseVisible = true;\n\t}\n\n\tprotected override void LoadContent()\n\t{\n\t\t_spriteBatch = new SpriteBatch(GraphicsDevice);\n\t\t$0\n\t}\n\n\tprotected override void Update(GameTime gameTime)\n\t{\n\t\tbase.Update(gameTime);\n\t}\n\n\tprotected override void Draw(GameTime gameTime)\n\t{\n\t\tGraphicsDevice.Clear(Color.CornflowerBlue);\n\t\tbase.Draw(gameTime);\n\t}\n}' },
            { label: 'override Initialize', detail: 'Initialize override', text: 'protected override void Initialize()\n{\n\t$0\n\tbase.Initialize();\n}' },
            { label: 'override LoadContent', detail: 'LoadContent override', text: 'protected override void LoadContent()\n{\n\t$0\n}' },
            { label: 'override Update', detail: 'Update override', text: 'protected override void Update(GameTime gameTime)\n{\n\t$0\n\tbase.Update(gameTime);\n}' },
            { label: 'override Draw', detail: 'Draw override', text: 'protected override void Draw(GameTime gameTime)\n{\n\tGraphicsDevice.Clear(Color.CornflowerBlue);\n\t$0\n\tbase.Draw(gameTime);\n}' },
            { label: 'spritebatch block', detail: 'SpriteBatch Begin/End', text: '_spriteBatch.Begin();\n$0\n_spriteBatch.End();' },
        ];

        // Build a lookup for dot-completion: word before dot -> suggestions
        var memberLookup = {};
        Object.keys(xnaTypes).forEach(function (typeName) {
            var lower = typeName.toLowerCase();
            memberLookup[lower] = { typeName: typeName, info: xnaTypes[typeName] };
        });
        // Common variable name -> type mappings
        var varAliases = {
            '_graphics': 'GraphicsDeviceManager', 'graphics': 'GraphicsDeviceManager',
            '_spritebatch': 'SpriteBatch', 'spritebatch': 'SpriteBatch', '_sb': 'SpriteBatch', 'sb': 'SpriteBatch',
            'graphicsdevice': 'GraphicsDevice', '_graphicsdevice': 'GraphicsDevice',
            'content': 'ContentManager', '_content': 'ContentManager',
            'gametime': 'GameTime', 'gt': 'GameTime',
            'mouse': 'Mouse', 'keyboard': 'Keyboard',
            'mousestate': 'MouseState', '_mousestate': 'MouseState',
            'keyboardstate': 'KeyboardState', '_keyboardstate': 'KeyboardState',
            'viewport': 'Viewport',
            '_shapebatch': 'ShapeBatch', 'shapebatch': 'ShapeBatch',
            'window': 'GameWindow',
        };

        monaco.languages.registerCompletionItemProvider('csharp', {
            triggerCharacters: ['.'],
            provideCompletionItems: function (model, position) {
                var word = model.getWordUntilPosition(position);
                var range = {
                    startLineNumber: position.lineNumber,
                    startColumn: word.startColumn,
                    endLineNumber: position.lineNumber,
                    endColumn: word.endColumn
                };

                // Check if this is dot-completion
                var lineContent = model.getLineContent(position.lineNumber);
                var textBefore = lineContent.substring(0, position.column - 1);

                // 'using' directive: complete namespaces
                var usingPrefixMatch = textBefore.match(/^\s*using\s+/);
                if (usingPrefixMatch && /^[\w.]*$/.test(textBefore.substring(usingPrefixMatch[0].length))) {
                    var nsTyped = textBefore.substring(usingPrefixMatch[0].length);
                    var nsStartColumn = usingPrefixMatch[0].length + 1; // 1-based column where namespace starts
                    var nsRange = {
                        startLineNumber: position.lineNumber,
                        startColumn: nsStartColumn,
                        endLineNumber: position.lineNumber,
                        endColumn: position.column
                    };
                    // filterText = only the segment after the last dot, so Monaco's prefix
                    // filter matches the word at cursor (e.g. "Fr" matches "Framework")
                    var lastDot = nsTyped.lastIndexOf('.');
                    var nsPrefix = lastDot >= 0 ? nsTyped.substring(0, lastDot + 1) : '';
                    var nsSuggestions = knownNamespaces
                        .filter(function (ns) { return ns.toLowerCase().startsWith(nsTyped.toLowerCase()); })
                        .map(function (ns) {
                            return {
                                label: ns,
                                kind: Kind.Module,
                                filterText: ns.substring(nsPrefix.length),
                                insertText: ns,
                                range: nsRange,
                                detail: 'namespace',
                                sortText: '0' + ns
                            };
                        });
                    return { suggestions: nsSuggestions };
                }

                var dotMatch = textBefore.match(/(\w+)\.\s*$/);

                if (dotMatch) {
                    var prefix = dotMatch[1].toLowerCase();
                    var typeName = varAliases[prefix];
                    var entry = typeName ? memberLookup[typeName.toLowerCase()] : memberLookup[prefix];

                    // Also try: if prefix matches a type property that returns a known type
                    // e.g., "ElapsedGameTime." -> TimeSpan members
                    if (!entry) {
                        var propToType = {
                            'elapsedgametime': 'TimeSpan', 'totalgametime': 'TimeSpan',
                            'viewport': 'Viewport', 'graphicsdevice': 'GraphicsDevice',
                            'bounds': 'Rectangle', 'location': 'Point', 'center': 'Point',
                        };
                        var mapped = propToType[prefix];
                        if (mapped) entry = memberLookup[mapped.toLowerCase()];
                    }

                    if (entry) {
                        return {
                            suggestions: entry.info.members.map(function (m) {
                                return {
                                    label: m,
                                    kind: Kind.Field,
                                    insertText: m,
                                    range: range,
                                    sortText: '0' + m
                                };
                            })
                        };
                    }
                    return { suggestions: [] };
                }

                // General completions: keywords + type names + snippets
                var suggestions = [];

                csharpKeywords.forEach(function (kw) {
                    suggestions.push({
                        label: kw,
                        kind: Kind.Keyword,
                        insertText: kw,
                        range: range,
                        sortText: '2' + kw
                    });
                });

                Object.keys(xnaTypes).forEach(function (typeName) {
                    suggestions.push({
                        label: typeName,
                        kind: xnaTypes[typeName].kind,
                        insertText: typeName,
                        range: range,
                        detail: 'XNA/KNI',
                        sortText: '1' + typeName
                    });
                });

                snippets.forEach(function (s) {
                    suggestions.push({
                        label: s.label,
                        kind: Kind.Snippet,
                        insertText: s.text,
                        insertTextRules: Insert,
                        range: range,
                        detail: s.detail,
                        sortText: '0' + s.label
                    });
                });

                return { suggestions: suggestions };
            }
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
                        var base64 = reader.result.split(',')[1];
                        window.fileDropInterop._dotNetRef.invokeMethodAsync('OnFileDropped', file.name, base64);
                    };
                    reader.readAsDataURL(file);
                })(files[i]);
            }
        });
    }
};
