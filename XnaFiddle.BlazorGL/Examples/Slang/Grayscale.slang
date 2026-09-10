// Slang port of ../Grayscale.Grayscale.fx — the same pixel-only SpriteBatch grayscale effect,
// written in HLSL-compatible Slang instead of HLSL. Proof of concept for the .slang compile
// pipeline (CompileRegisteredShadersAsync -> ShadowDusk.Compiler.Slang.SlangFrontend.ConvertToFx
// -> WasmShaderCompiler), verified headlessly via ShadowDuskCLI. Not yet wired into the example
// gallery or the [+] tab flow's default content — that's the next slice (a per-card HLSL/Slang
// toggle), so this file deliberately sits outside Examples/'s flat *.* glob and isn't embedded
// or auto-loaded today.
Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(SpriteTextureSampler, uv) * color;
    col.rgb = (col.r + col.g + col.b) / 3.0f;
    return col;
}
