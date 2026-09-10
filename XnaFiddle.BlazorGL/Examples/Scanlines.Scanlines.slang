// CRT scanline effect for SpriteBatch.
Texture2D SpriteTexture;
SamplerState s0;

float _attenuation; // how dark the lines get
float _linesFactor; // how many lines fit down the screen

[shader("fragment")]
float4 mainPS(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(s0, uv);
    float scanline = sin(uv.y * _linesFactor) * _attenuation;
    col.rgb -= scanline;
    return col;
}
