// Slang port of Grayscale.Grayscale.fx — the same pixel-only SpriteBatch grayscale effect,
// written in HLSL-compatible Slang instead of HLSL. The "Grayscale" gallery example ships both
// and opens exactly one as a tab, chosen by the example browser's shader-language toggle: both
// register their compiled effect under the bare name "Grayscale" via RegisterContentFile, so
// opening both at once would collide. Compiled through CompileRegisteredShadersAsync ->
// ShadowDusk.Compiler.Slang.SlangFrontend.ConvertToFx -> WasmShaderCompiler, exactly like a
// .fx tab once converted.
Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(SpriteTextureSampler, uv) * color;
    col.rgb = (col.r + col.g + col.b) / 3.0f;
    return col;
}
