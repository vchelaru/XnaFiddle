using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace XnaFiddle
{
    /// <summary>
    /// Represents a compact snippet JSON that describes only the interesting parts of a game.
    /// XnaFiddle wraps it in a full Game subclass scaffold before compiling.
    /// </summary>
    public class SnippetModel
    {
        /// <summary>Inject Gum UI boilerplate (GumService init/update/draw).</summary>
        [JsonPropertyName("IsGum")]
        public bool IsGum { get; set; }

        /// <summary>Inject Apos.Shapes boilerplate (ShapeBatch creation, Begin/End).</summary>
        [JsonPropertyName("IsAposShapes")]
        public bool IsAposShapes { get; set; }

        /// <summary>Inject MonoGame.Extended boilerplate (SpriteBatch creation).</summary>
        [JsonPropertyName("IsMonoGameExtended")]
        public bool IsMonoGameExtended { get; set; }

        /// <summary>Additional using namespaces beyond the defaults and preset ones.</summary>
        [JsonPropertyName("usings")]
        public List<string> Usings { get; set; }

        /// <summary>Extra field/property declarations inside the game class.</summary>
        [JsonPropertyName("members")]
        public string Members { get; set; }

        /// <summary>Code placed inside Initialize(), after base.Initialize() and preset init.</summary>
        [JsonPropertyName("initialize")]
        public string Initialize { get; set; }

        /// <summary>Code placed inside LoadContent(), after preset LoadContent lines.</summary>
        [JsonPropertyName("loadContent")]
        public string LoadContent { get; set; }

        /// <summary>Code placed inside Update(), after preset pre-update lines.</summary>
        [JsonPropertyName("update")]
        public string Update { get; set; }

        /// <summary>Code placed inside Draw(), between preset Begin/End wrappers.</summary>
        [JsonPropertyName("draw")]
        public string Draw { get; set; }
    }
}
