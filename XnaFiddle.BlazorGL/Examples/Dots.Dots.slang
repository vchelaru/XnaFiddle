// Halftone dot effect for SpriteBatch.
Texture2D SpriteTexture;
SamplerState s0;

float angle;        // rotation of the dot grid, in radians
float scale;        // dot frequency; higher = smaller, denser dots
float2 ScreenSize;  // pixel size of the drawn area, so the dots stay square
float Intensity;    // 0 = original image, 1 = full (hard black/white) halftone

float pattern(float angle, float2 uv, float scale)
{
    float s = sin(angle);
    float c = cos(angle);
    float2 tex = uv * ScreenSize;
    float2 pt = float2(c * tex.x - s * tex.y, s * tex.x + c * tex.y) * scale;
    return (sin(pt.x) * sin(pt.y)) * 4.0;
}

[shader("fragment")]
float4 PixelShaderFunction(float4 position : SV_Position, float4 color : COLOR0, float2 uv : TEXCOORD0) : SV_Target
{
    float4 col = SpriteTexture.Sample(s0, uv);
    float average = (col.r + col.g + col.b) / 3.0;
    float val = average * 10.0 - 5.0 + pattern(angle, uv, scale);
    float dots = saturate(val);                  // hard halftone mask (0 or 1)
    float4 halftone = float4(dots, dots, dots, col.a);
    return lerp(col, halftone, Intensity);       // blend over the original to tame it
}
