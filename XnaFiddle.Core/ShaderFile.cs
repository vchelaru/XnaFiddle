using System.Text.Json.Serialization;

namespace XnaFiddle
{
    /// <summary>
    /// One shader (.fx) editor tab carried in share / snippet / gist payloads so shaders
    /// round-trip alongside the C# program. <see cref="Name"/> is the tab filename (the
    /// Content.Load&lt;Effect&gt; key, including the .fx extension); <see cref="Source"/> is
    /// the HLSL text. See issue #26.
    /// </summary>
    public class ShaderFile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; }
    }
}
