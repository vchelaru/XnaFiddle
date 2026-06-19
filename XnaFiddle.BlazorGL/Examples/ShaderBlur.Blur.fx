#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;

sampler2D SpriteTextureSampler = sampler_state
{
    Texture = <SpriteTexture>;
};

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

technique BasicColorDrawing
{
	pass P0
	{
		PixelShader = compile PS_SHADERMODEL MainPS();
	}
};
