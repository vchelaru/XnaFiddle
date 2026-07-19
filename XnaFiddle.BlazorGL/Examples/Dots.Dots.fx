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

float angle; // 0.5
float scale; // 0.5
float2 ScreenSize;
float Intensity; // 0 = original image, 1 = full (hard black/white) halftone

float pattern( float angle, float2 uv, float scale )
{
   float s = sin( angle );
   float c = cos( angle );
   float2 tex = uv * ScreenSize;
   float2 pt = float2( c * tex.x - s * tex.y, s * tex.x + c * tex.y ) * scale;
   return ( sin( pt.x ) * sin( pt.y ) ) * 4.0;
}

struct VertexShaderOutput
{
	float4 Position : SV_POSITION;
	float4 Color    : COLOR0;
	float2 TexCoord : TEXCOORD0;
};

// ShadowDusk's OpenGL translator (and the real MonoGame Content Builder's DXC pass) rewrite/validate
// the pixel shader entry point by matching a literal, unconditioned `: COLOR`/`tex2D(...)` shape --
// splitting the signature or a call site across an #if breaks that (verified against the real
// ShadowDuskCLI tool and a real DX12 Content Builder build while fixing issue #52's SM6 gap: SM6
// also requires `SV_Target0` instead of `COLOR`). So the two shader models get fully separate,
// self-contained entry points instead of one shared function with an internal #if.
#if defined(SM6) || defined(VULKAN)
float4 PixelShaderFunction(VertexShaderOutput input) : SV_Target0
{
    float4 color = SpriteTexture.Sample( s0, input.TexCoord );
    float average = ( color.r + color.g + color.b ) / 3.0;
    float val = average * 10.0 - 5.0 + pattern( angle, input.TexCoord, scale );
    float dots = saturate( val );                  // hard halftone mask (0 or 1)
    float4 halftone = float4( dots, dots, dots, color.a );
    return lerp( color, halftone, Intensity );     // blend over the original to tame it
}
#else
float4 PixelShaderFunction(VertexShaderOutput input) : COLOR
{
    float4 color = tex2D( s0, input.TexCoord );
    float average = ( color.r + color.g + color.b ) / 3.0;
    float val = average * 10.0 - 5.0 + pattern( angle, input.TexCoord, scale );
    float dots = saturate( val );                  // hard halftone mask (0 or 1)
    float4 halftone = float4( dots, dots, dots, color.a );
    return lerp( color, halftone, Intensity );     // blend over the original to tame it
}
#endif

technique Technique1
{
    pass Pass1
    {
        PixelShader = compile PS_SHADERMODEL PixelShaderFunction();
    }
}
