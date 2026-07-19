#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#elif defined(SM6) || defined(VULKAN)
	// MonoGame's native Content Builder (DX12/Vulkan) compiles via DXC, which requires Shader
	// Model 6 and rejects the classic sampler2D/tex2D combo below -- see the Texture2D/SamplerState
	// branch and the SM6 sampling call site further down.
	#define VS_SHADERMODEL vs_6_0
	#define PS_SHADERMODEL ps_6_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#if defined(SM6) || defined(VULKAN)
Texture2D<float4> SpriteTexture : register(t0);
SamplerState SpriteTextureSampler : register(s0);
#else
Texture2D SpriteTexture;

sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};
#endif

// ---- QUALITY KNOB -------------------------------------------------------
// SampleCount is how many taps we take on each side of the center. More taps
// fill the gaps between samples, so a strong blur stays smooth instead of
// breaking into faint stripes (the under-sampling artifact). It must be a
// compile-time constant so the GPU can unroll the loop below -- but XnaFiddle
// recompiles this .fx every time you press Run, so just change the number and
// re-Run. Higher = smoother and more GPU work; lower = faster but more banding.
static const int SampleCount = 8;

// Gaussian falloff shape, derived from the tap count. Smaller = peakier.
static const float Sigma = SampleCount / 2.0f;
// -------------------------------------------------------------------------

// Total blur reach for this pass, in texture-coordinate units. The C# code
// sets this per pass: (BlurRadius / width, 0) horizontally, then
// (0, BlurRadius / height) vertically. Doing one axis at a time is what makes
// the blur "separable" -- two cheap 1D passes instead of one costly 2D kernel.
float2 Offset;

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

// ShadowDusk's OpenGL translator (and the real MonoGame Content Builder's DXC pass) rewrite/validate
// the pixel shader entry point by matching a literal, unconditioned `: COLOR`/`tex2D(...)` shape --
// splitting the signature or a call site across an #if breaks that (verified against the real
// ShadowDuskCLI tool and a real DX12 Content Builder build while fixing issue #52's SM6 gap: SM6
// also requires `SV_Target0` instead of `COLOR`). So the two shader models get fully separate,
// self-contained entry points instead of one shared function with an internal #if.
#if defined(SM6) || defined(VULKAN)
float4 MainPS(VertexShaderOutput input) : SV_Target0
{
    float2 uv = input.TextureCoordinates;
    float2 tapStep = Offset / SampleCount; // spacing between neighbouring taps

    // Accumulate Gaussian-weighted samples and divide by the total weight at
    // the end, so the blur preserves brightness for any SampleCount / Sigma.
    float total = 0.0f;
    float4 sum = 0.0f;
    for (int i = -SampleCount; i <= SampleCount; i++)
    {
        float weight = exp(-(i * i) / (2.0f * Sigma * Sigma));
        sum += SpriteTexture.Sample(SpriteTextureSampler, uv + tapStep * i) * weight;
        total += weight;
    }

    return (sum / total) * input.Color;
}
#else
float4 MainPS(VertexShaderOutput input) : COLOR
{
    float2 uv = input.TextureCoordinates;
    float2 tapStep = Offset / SampleCount; // spacing between neighbouring taps

    // Accumulate Gaussian-weighted samples and divide by the total weight at
    // the end, so the blur preserves brightness for any SampleCount / Sigma.
    float total = 0.0f;
    float4 sum = 0.0f;
    for (int i = -SampleCount; i <= SampleCount; i++)
    {
        float weight = exp(-(i * i) / (2.0f * Sigma * Sigma));
        sum += tex2D(SpriteTextureSampler, uv + tapStep * i) * weight;
        total += weight;
    }

    return (sum / total) * input.Color;
}
#endif

technique BasicColorDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
