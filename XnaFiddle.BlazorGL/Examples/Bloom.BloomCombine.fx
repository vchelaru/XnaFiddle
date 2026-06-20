#if OPENGL
	#define SV_POSITION POSITION
	#define VS_SHADERMODEL vs_3_0
	#define PS_SHADERMODEL ps_3_0
#else
	#define VS_SHADERMODEL vs_4_0_level_9_1
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

Texture2D SpriteTexture;                 // slot 0 — SpriteBatch binds the drawn (BLOOM) texture here
sampler2D BloomSampler = sampler_state { Texture = <SpriteTexture>; };

Texture2D BaseTexture;                    // the original scene, set from C# as a parameter
sampler2D BaseSampler = sampler_state { Texture = <BaseTexture>; };

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

technique BasicColorDrawing { pass P0 { PixelShader = compile PS_SHADERMODEL MainPS(); } }
