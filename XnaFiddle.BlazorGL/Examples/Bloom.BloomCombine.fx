#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#elif defined(SM6) || defined(VULKAN)
	// MonoGame's native Content Builder (DX12/Vulkan) compiles via DXC, which requires Shader
	// Model 6 and rejects the classic sampler2D/tex2D combo below -- see the Texture2D/SamplerState
	// branch and the SM6 sampling call sites further down.
	#define VS_SHADERMODEL vs_6_0
	#define PS_SHADERMODEL ps_6_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#if defined(SM6) || defined(VULKAN)
Texture2D<float4> SpriteTexture : register(t0);                 // slot 0 — SpriteBatch binds the drawn (BLOOM) texture here
SamplerState BloomSampler : register(s0);

Texture2D<float4> BaseTexture : register(t1);                    // the original scene, set from C# as a parameter
SamplerState BaseSampler : register(s1);
#else
Texture2D SpriteTexture;                 // slot 0 — SpriteBatch binds the drawn (BLOOM) texture here
sampler2D BloomSampler = sampler_state { Texture = <SpriteTexture>; };

Texture2D BaseTexture;                    // the original scene, set from C# as a parameter
sampler2D BaseSampler = sampler_state { Texture = <BaseTexture>; };
#endif

float BloomIntensity;
float BaseIntensity;
float BloomSaturation;
float BaseSaturation;

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float2 TextureCoordinates : TEXCOORD0;
};

// Push a color toward (saturation<1) or away from (>1) gray. 1 = unchanged.
float3 AdjustSaturation(float3 color, float saturation)
{
    float grey = dot(color, float3(0.3, 0.59, 0.11));
    return lerp(grey.xxx, color, saturation);
}

// ShadowDusk's OpenGL translator (and the real MonoGame Content Builder's DXC pass) rewrite/validate
// the pixel shader entry point by matching a literal, unconditioned `: COLOR`/`tex2D(...)` shape --
// splitting the signature or a call site across an #if breaks that (verified against the real
// ShadowDuskCLI tool and a real DX12 Content Builder build while fixing issue #52's SM6 gap: SM6
// also requires `SV_Target0` instead of `COLOR`). So the two shader models get fully separate,
// self-contained entry points instead of one shared function with an internal #if.
#if defined(SM6) || defined(VULKAN)
float4 MainPS(VertexShaderOutput input) : SV_Target0
{
    float3 bloom = SpriteTexture.Sample(BloomSampler, input.TextureCoordinates).rgb;
    float3 base  = BaseTexture.Sample(BaseSampler, input.TextureCoordinates).rgb;

    bloom = AdjustSaturation(bloom, BloomSaturation) * BloomIntensity;
    base  = AdjustSaturation(base,  BaseSaturation)  * BaseIntensity;

    // Darken the base where the bloom is strong so bright glows don't wash out to white,
    // then add. With the intensities at 1 this is a screen-style blend that stays in
    // [0,1] and cannot clip — which is what keeps the glow the right hue.
    base *= (1.0 - saturate(bloom));

    return float4(base + bloom, 1.0);
}
#else
float4 MainPS(VertexShaderOutput input) : COLOR
{
    float3 bloom = tex2D(BloomSampler, input.TextureCoordinates).rgb;
    float3 base  = tex2D(BaseSampler,  input.TextureCoordinates).rgb;

    bloom = AdjustSaturation(bloom, BloomSaturation) * BloomIntensity;
    base  = AdjustSaturation(base,  BaseSaturation)  * BaseIntensity;

    // Darken the base where the bloom is strong so bright glows don't wash out to white,
    // then add. With the intensities at 1 this is a screen-style blend that stays in
    // [0,1] and cannot clip — which is what keeps the glow the right hue.
    base *= (1.0 - saturate(bloom));

    return float4(base + bloom, 1.0);
}
#endif

technique BasicColorDrawing { pass P0 { PixelShader = compile PS_SHADERMODEL MainPS(); } }
