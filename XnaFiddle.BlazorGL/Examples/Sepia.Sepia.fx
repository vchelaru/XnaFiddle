#if OPENGL
	#define SV_POSITION POSITION
	#define PS_SHADERMODEL ps_3_0
#elif defined(SM6) || defined(VULKAN)
	// MonoGame's native Content Builder (DX12/Vulkan) compiles via DXC, which requires Shader
	// Model 6 and rejects the classic implicit `sampler s0;` binding below -- see the
	// Texture2D/SamplerState branch and the SM6 sampling call site further down.
	#define PS_SHADERMODEL ps_6_0
#else
	#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#if defined(SM6) || defined(VULKAN)
Texture2D<float4> SpriteTexture : register(t0);   // SpriteBatch's slot-0 texture; s0 has no Texture2D in the legacy binding below
SamplerState s0 : register(s0);
#else
sampler s0;
#endif
float3 _sepiaTone; // defaults to 1.2, 1.0, 0.8

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color    : COLOR0;
	float2 TexCoord : TEXCOORD0;
};

// ShadowDusk's OpenGL translator (and the real MonoGame Content Builder's DXC pass) rewrite/validate
// the pixel shader entry point by matching a literal, unconditioned `: COLOR0`/`tex2D(...)` shape --
// splitting the signature or a call site across an #if breaks that (verified against the real
// ShadowDuskCLI tool and a real DX12 Content Builder build while fixing issue #52's SM6 gap: SM6
// also requires `SV_Target0` instead of `COLOR0`). So the two shader models get fully separate,
// self-contained entry points instead of one shared function with an internal #if.
#if defined(SM6) || defined(VULKAN)
float4 PixelShaderFunction( VertexShaderOutput input ) : SV_Target0
{
    float4 tex = SpriteTexture.Sample( s0, input.TexCoord );

    // first we need to convert to greyscale
    float grayScale = dot( tex.rgb, float3( 0.3, 0.59, 0.11 ) );

    tex.rgb = grayScale * _sepiaTone;

    return tex;
}
#else
float4 PixelShaderFunction( VertexShaderOutput input ) : COLOR0
{
    float4 tex = tex2D( s0, input.TexCoord );

    // first we need to convert to greyscale
    float grayScale = dot( tex.rgb, float3( 0.3, 0.59, 0.11 ) );

    tex.rgb = grayScale * _sepiaTone;

    return tex;
}
#endif

technique Technique1
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction();
    }
}
