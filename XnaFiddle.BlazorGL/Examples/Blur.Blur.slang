// Separable Gaussian blur for SpriteBatch: run once horizontally, then once vertically.
Texture2D SpriteTexture;
SamplerState SpriteTextureSampler;

// How many taps are taken on each side of the center. More taps keep a strong blur smooth
// instead of breaking into faint stripes; fewer are cheaper but band. Must be a compile-time
// constant so the loop below can unroll.
static const int SampleCount = 8;

// Gaussian falloff shape, derived from the tap count. Smaller = peakier.
static const float Sigma = SampleCount / 2.0f;

// Total blur reach for this pass, in texture-coordinate units. The C# code sets it per pass:
// (BlurRadius / width, 0) horizontally, then (0, BlurRadius / height) vertically. One axis at
// a time is what makes the blur "separable" -- two cheap 1D passes instead of one 2D kernel.
float2 Offset;

[shader("fragment")]
float4 MainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
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

    return (sum / total) * color;
}
