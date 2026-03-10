using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace XnaFiddle
{
    /// <summary>
    /// Expands a <see cref="SnippetModel"/> into a complete, compilable C# Game subclass.
    /// Preset flags (IsGum, IsAposShapes, IsMonoGameExtended) inject their required
    /// boilerplate automatically; the user only provides the interesting parts.
    /// </summary>
    public static class SnippetExpander
    {
        static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static string Expand(string snippetJson)
        {
            var model = JsonSerializer.Deserialize<SnippetModel>(snippetJson, _jsonOptions);
            return Expand(model);
        }

        public static string Expand(SnippetModel model)
        {
            // ── Accumulate contributions from presets ─────────────────────────────
            var usings           = new List<string> { "System", "Microsoft.Xna.Framework",
                                                      "Microsoft.Xna.Framework.Graphics",
                                                      "Microsoft.Xna.Framework.Input" };
            var members          = new List<string>();
            var postInitialize   = new List<string>();
            var loadContentLines = new List<string>();
            var preUpdate        = new List<string>();
            var preDraw          = new List<string>();
            var postDraw         = new List<string>();
            string clearColor    = "Color.CornflowerBlue";

            // ── Gum preset ────────────────────────────────────────────────────────
            // Sets up GumService: init after base.Initialize(), update every frame,
            // draw after user draw code so UI renders on top.
            if (model.IsGum)
            {
                usings.Add("MonoGameGum");
                usings.Add("Gum.Forms");
                usings.Add("Gum.Forms.Controls");
                members.Add("GumService GumUI => GumService.Default;");
                postInitialize.Add("GumUI.Initialize(this, DefaultVisualsVersion.V3);");
                preUpdate.Add("GumUI.Update(gameTime);");
                clearColor = "new Color(0.15f, 0.15f, 0.2f)";
                postDraw.Add("GumUI.Draw();");
            }

            // ── Apos.Shapes preset ────────────────────────────────────────────────
            // Creates ShapeBatch in LoadContent; wraps user draw code in Begin/End.
            // Inserted before GumUI.Draw so shapes render beneath the UI layer.
            if (model.IsAposShapes)
            {
                usings.Add("Apos.Shapes");
                members.Add("ShapeBatch _shapeBatch;");
                loadContentLines.Add("_shapeBatch = new ShapeBatch(GraphicsDevice, Content);");
                if (!model.IsGum) clearColor = "new Color(0.1f, 0.1f, 0.15f)";
                preDraw.Add("_shapeBatch.Begin();");
                postDraw.Insert(0, "_shapeBatch.End();"); // before GumUI.Draw if combined
            }

            // ── MonoGame.Extended preset ──────────────────────────────────────────
            // Creates SpriteBatch in LoadContent. No auto Begin/End — Extended users
            // almost always need custom Begin() args (e.g. transformMatrix for camera),
            // so that is left to the user's draw snippet.
            if (model.IsMonoGameExtended)
            {
                usings.Add("MonoGame.Extended");
                members.Add("SpriteBatch _spriteBatch;");
                loadContentLines.Add("_spriteBatch = new SpriteBatch(GraphicsDevice);");
                if (!model.IsGum && !model.IsAposShapes) clearColor = "new Color(18, 18, 28)";
            }

            // ── User-supplied extra usings ────────────────────────────────────────
            if (model.Usings != null)
                usings.AddRange(model.Usings);

            // ── Build the source ──────────────────────────────────────────────────
            var sb = new StringBuilder();

            foreach (var u in usings)
                sb.AppendLine($"using {u};");
            sb.AppendLine();

            sb.AppendLine("public class FiddleGame : Game");
            sb.AppendLine("{");
            sb.AppendLine("    GraphicsDeviceManager graphics;");
            foreach (var m in members)
                sb.AppendLine($"    {m}");
            if (!string.IsNullOrWhiteSpace(model.Members))
                AppendIndented(sb, model.Members, 4);
            sb.AppendLine();

            // Constructor
            sb.AppendLine("    public FiddleGame()");
            sb.AppendLine("    {");
            sb.AppendLine("        graphics = new GraphicsDeviceManager(this);");
            sb.AppendLine("        graphics.GraphicsProfile = GraphicsProfile.HiDef;");
            sb.AppendLine("        IsMouseVisible = true;");
            sb.AppendLine("        Window.AllowUserResizing = true;");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Initialize
            sb.AppendLine("    protected override void Initialize()");
            sb.AppendLine("    {");
            sb.AppendLine("        base.Initialize();");
            foreach (var line in postInitialize)
                sb.AppendLine($"        {line}");
            if (!string.IsNullOrWhiteSpace(model.Initialize))
                AppendIndented(sb, model.Initialize, 8);
            sb.AppendLine("    }");
            sb.AppendLine();

            // LoadContent (omitted if nothing to put in it)
            bool hasLoadContent = loadContentLines.Count > 0 || !string.IsNullOrWhiteSpace(model.LoadContent);
            if (hasLoadContent)
            {
                sb.AppendLine("    protected override void LoadContent()");
                sb.AppendLine("    {");
                foreach (var line in loadContentLines)
                    sb.AppendLine($"        {line}");
                if (!string.IsNullOrWhiteSpace(model.LoadContent))
                    AppendIndented(sb, model.LoadContent, 8);
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            // Update
            sb.AppendLine("    protected override void Update(GameTime gameTime)");
            sb.AppendLine("    {");
            foreach (var line in preUpdate)
                sb.AppendLine($"        {line}");
            if (!string.IsNullOrWhiteSpace(model.Update))
                AppendIndented(sb, model.Update, 8);
            sb.AppendLine("        base.Update(gameTime);");
            sb.AppendLine("    }");
            sb.AppendLine();

            // Draw
            sb.AppendLine("    protected override void Draw(GameTime gameTime)");
            sb.AppendLine("    {");
            sb.AppendLine($"        GraphicsDevice.Clear({clearColor});");
            foreach (var line in preDraw)
                sb.AppendLine($"        {line}");
            if (!string.IsNullOrWhiteSpace(model.Draw))
                AppendIndented(sb, model.Draw, 8);
            foreach (var line in postDraw)
                sb.AppendLine($"        {line}");
            sb.AppendLine("        base.Draw(gameTime);");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            return sb.ToString();
        }

        static void AppendIndented(StringBuilder sb, string code, int indent)
        {
            string pad = new string(' ', indent);
            foreach (var line in code.Split('\n'))
                sb.AppendLine(pad + line.TrimEnd('\r'));
        }
    }
}
