// Based on the original Shadertoy ocean shader by afl_ext (2017-2024).
// Original source: https://www.shadertoy.com/view/MdXyzX
// Original license: MIT License
//
// Port/adaptations in this file:
// - MonoGame fullscreen-quad port with normalized texcoord input and Y-flip handling.
// - Camera rotation driven by MonoGame CameraAngles instead of Shadertoy mouse input.
// - Retuned celestial motion for the demo plus added moon, stars, and night-sky transitions.
// - Documentation notes marking where the port intentionally diverges from the reference shader.

#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_1
    #define PS_SHADERMODEL ps_4_0_level_9_1
#endif

#define DRAG_MULT 0.25          // changes how much waves pull on the water
#define WATER_DEPTH 1.0         // how deep is the water
#define ITERATIONS_RAYMARCH 12  // waves iterations or raymarching
#define ITERATIONS_NORMAL 36    // waves iterations when calculating normals

float2 Resolution;     // supplied by the MonoGame host; fills the same role as Shadertoy's iResolution
float3 CameraPosition; // port-specific addition for the MonoGame camera position
float2 CameraAngles;   // port-specific addition for the MonoGame camera yaw/pitch in place of Shadertoy mouse input
float Time;            // supplied by the MonoGame host; fills the same role as Shadertoy's iTime

struct VSInput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
};

VSOutput MainVS(VSInput input)
{
    VSOutput output;
    output.Position = input.Position;
    output.TexCoord = input.TexCoord;
    return output;
}

// Calculates wave value and its derivative,
// for the wave direction, position in space, wave frequency and time
float2 wavedx(float2 position, float2 direction, float frequency, float timeshift)
{
    float x = dot(direction, position) * frequency + timeshift;
    float wave = exp(sin(x) - 1.0f);
    float dx = wave * cos(x);
    return float2(wave, -dx);
}

// Calculates waves by summing octaves of various waves with various parameters
float getwaves(float2 position, int iterations)
{
    float wavePhaseShift = length(position) * 0.1f; // this is to avoid every octave having exactly the same phase everywhere
    float iter = 0.0f;                              // this will help generating well distributed wave directions
    float frequency = 1.0f;                         // frequency of the wave, this will change every iteration
    float timeMultiplier = 2.0f;                    // time multiplier for the wave, this will change every iteration
    float weight = 1.0f;                            // weight in final sum for hte wave, this will change every iteration
    float sumOfValues = 0.0f;                       // wills tore final sum of values
    float sumOfWeights = 0.0f;                      // will store final sum of weights

    [loop]
    for (int i = 0; i < iterations; i++)
    {
        // generate some wave direction that looks kind of random
        float2 p = float2(sin(iter), cos(iter));

        // calculate wave data
        float2 res = wavedx(position, p, frequency, Time * timeMultiplier + wavePhaseShift);

        // shift position around according to wave drag and derivative of the wave
        position += p * res.y * weight * DRAG_MULT;

        // add the results to sums
        sumOfValues += res.x * weight;
        sumOfWeights += weight;

        // modify next octave
        weight = lerp(weight, 0.0f, 0.2f);
        frequency *= 1.18f;
        timeMultiplier *= 1.07f;

        // add some kind of random value to make next wave look random too
        iter += 1232.399963f;
    }

    // calculate and return
    return sumOfValues / sumOfWeights;
}

// Raymarches the ray from top water layer boundary to low water layer boundary
float raymarchwater(float3 camera, float3 start, float3 end, float depth)
{
    float3 pos = start;
    float3 dir = normalize(end - start);

    [loop]
    for (int i = 0; i < 64; i++)
    {
        // the height is from 0 to -depth
        float height = getwaves(pos.xz, ITERATIONS_RAYMARCH) * depth - depth;

        // if the waves height almost nearly matches the ray height, assume its a hit and return the hit distance
        if (height + 0.01f > pos.y)
        {
            return distance(pos, camera);
        }

        // iterate forwards according to the height mismatch
        pos += dir * (pos.y - height);
    }

    // if hit was not registered, just assume hit the top layer,
    // this makes the raymarching faster and looks better at higher distances
    return distance(start, camera);
}

// Calculate normal at point by calculating the height at the pos and 2 additional points very close to pos
float3 normal(float2 pos, float e, float depth)
{
    float2 ex = float2(e, 0.0f);
    float H = getwaves(pos, ITERATIONS_NORMAL) * depth;
    float3 a = float3(pos.x, H, pos.y);
    return normalize(
        cross(
            a - float3(pos.x - e, getwaves(pos.xy - ex.xy, ITERATIONS_NORMAL) * depth, pos.y),
            a - float3(pos.x, getwaves(pos.xy + ex.yx, ITERATIONS_NORMAL) * depth, pos.y + e)
        )
    );
}

// Helper function generating a rotation matrix around the axis by the angle
float3x3 createRotationMatrixAxisAngle(float3 axis, float angle)
{
    float s = sin(angle);
    float c = cos(angle);
    float oc = 1.0f - c;
    return float3x3(
        oc * axis.x * axis.x + c, oc * axis.x * axis.y - axis.z * s, oc * axis.z * axis.x + axis.y * s,
        oc * axis.x * axis.y + axis.z * s, oc * axis.y * axis.y + c, oc * axis.y * axis.z - axis.x * s,
        oc * axis.z * axis.x - axis.y * s, oc * axis.y * axis.z + axis.x * s, oc * axis.z * axis.z + c);
}

// Helper function that generates camera ray based on UV and camera angles
float3 getRay(float2 fragCoord)
{
    // In the original Shadertoy shader, fragCoord is pixel-space and gets normalized by iResolution.
    // Here we receive MonoGame fullscreen quad texcoords that are already in 0..1 space, so we keep
    // the same mapping but skip the extra division. The Y flip is also intentional because MonoGame's
    // screen-space texture coordinate convention differs from the Shadertoy path this was ported from.
    float2 uv = float2(fragCoord.x, 1.0f - fragCoord.y);
    float2 screen = (uv * 2.0f - 1.0f) * float2(Resolution.x / Resolution.y, 1.0f);

    // for fisheye, uncomment following line and comment the next one
    //float3 proj = normalize(float3(screen.x, screen.y, 1.0f) + float3(screen.x, screen.y, -1.0f) * pow(length(screen), 2.0f) * 0.05f);
    float3 proj = normalize(float3(screen, 1.5f));

    if (Resolution.x < 600.0f)
    {
        return proj;
    }

    // The reference shader rotates directly from Shadertoy mouse input here.
    // In this MonoGame port, those angles are precomputed in C# and passed in as CameraAngles.
    return mul(
        createRotationMatrixAxisAngle(float3(0.0f, -1.0f, 0.0f), CameraAngles.x),
        mul(createRotationMatrixAxisAngle(float3(1.0f, 0.0f, 0.0f), CameraAngles.y), proj));
}

// Ray-Plane intersection checker
// The reference shader uses point and normal here. This port keeps pnt to avoid confusion with
// the existing normal(...) helper and the later water surface normal variable names.
float intersectPlane(float3 origin, float3 direction, float3 pnt, float3 normal)
{
    return clamp(dot(pnt - origin, normal) / dot(direction, normal), -1.0f, 9991999.0f);
}

// The reference shader only has getSunDirection() with one hard-coded sun path.
// This port adds a shared celestial path helper so the sun and moon can follow the same orbit
// while staying half a cycle apart. The constants differ from the reference because the orbit was
// retuned here for the MonoGame demo: flatter arc, equal day/night timing, and a slower visual pass.
float3 getCelestialDirection(float phaseOffset)
{
    float cycle = Time * 0.09f + 0.2f + phaseOffset;
    return normalize(float3(-0.55f, sin(cycle) * 0.75f, cos(cycle)));
}

// Calculate where the sun should be, it will be moving around the sky.
// In the reference shader this function directly returned a single tuned sun vector.
// Here it delegates to the shared orbit helper so the moon can reuse the same path.
float3 getSunDirection()
{
    return getCelestialDirection(0.0f);
}

// The reference shader has no moon direction helper.
// This was added in the port so the moon can rise and set on the same path as the sun,
// offset by pi radians to keep the cycle opposite across the sky.
float3 getMoonDirection()
{
    return getCelestialDirection(3.14159265f);
}

// The reference shader has no night transition helper because it only renders a sun-driven sky.
// This port adds getNightAmount() so the sun height can drive when moonlight, stars, and the
// darker night atmosphere terms fade in and out across the day/night cycle.
float getNightAmount(float sunHeight)
{
    return saturate((-sunHeight + 0.04f) * 2.6f);
}

// Some very barebones but fast atmosphere approximation
float3 extra_cheap_atmosphere(float3 raydir, float3 sundir)
{
    float up = saturate(raydir.y * 0.5f + 0.5f);
    float sunDot = max(0.0f, dot(sundir, raydir));
    float solarAltitude = saturate(sundir.y * 0.5f + 0.5f);
    float dayAmount = smoothstep(-0.1f, 0.16f, sundir.y);
    float nightAmount = getNightAmount(sundir.y);
    float twilightAmount = (1.0f - nightAmount) * (1.0f - smoothstep(0.03f, 0.2f, abs(sundir.y)));
    float horizon = pow(saturate(1.0f - max(raydir.y, 0.0f)), 2.2f);

    // These sky gradients are a port-specific addition for the day/night cycle and moonlit night sky.
    float3 daySky = lerp(float3(0.24f, 0.35f, 0.55f), float3(0.09f, 0.29f, 0.62f), up);
    float3 twilightSky = lerp(float3(0.48f, 0.24f, 0.18f), float3(0.06f, 0.1f, 0.24f), up);
    float3 nightSky = lerp(float3(0.01f, 0.02f, 0.05f), float3(0.0f, 0.005f, 0.02f), up);
    float3 sky = lerp(nightSky, daySky, dayAmount);
    sky = lerp(sky, twilightSky, twilightAmount * (1.0f - nightAmount));
    sky *= lerp(0.95f, 1.18f, solarAltitude);
    sky += float3(0.02f, 0.035f, 0.05f) * solarAltitude * up;

    // These are also port-specific additions that keep sunset warmth and a faint night horizon fill.
    float sunsetGlow = pow(sunDot, 10.0f);
    float3 glowColor = lerp(float3(1.0f, 0.94f, 0.9f), float3(1.0f, 0.68f, 0.4f), saturate((0.22f - sundir.y) * 3.0f));
    float3 mie = glowColor * sunsetGlow * (0.16f + horizon * 0.22f);
    float3 horizonFill = float3(0.08f, 0.11f, 0.18f) * horizon * nightAmount;

    return sky + mie + horizonFill;
}

// This small hash was added in the port to generate stable pseudo-random star placement per cell.
float hash21(float2 position)
{
    position = frac(position * float2(123.34f, 456.21f));
    position += dot(position, position + 45.32f);
    return frac(position.x * position.y);
}

// The reference shader has no star tint helper because it has no stars.
// This port adds it so the night sky can mix slightly warm and cool star colors instead of using one flat white tone.
float3 getStarTint(float seed)
{
    return lerp(float3(1.0f, 0.82f, 0.72f), float3(0.67f, 0.81f, 1.0f), saturate(seed * 1.2f));
}

// The reference shader has no star field function.
// This port adds getStars() so the night side of the sky can fill with stars once the sun drops low enough.
// It uses multiple star layers, each with different density, size, and twinkle, to avoid a uniform pattern.
float3 getStars(float3 direction)
{
    if (direction.y <= 0.0f)
    {
        return 0.0f.xxx;
    }

    float2 uv = direction.xz / (direction.y + 0.24f);

    // First star layer: medium-density background stars.
    float2 primaryUv = uv * 180.0f;
    float2 primaryCell = floor(primaryUv);
    float2 primaryLocal = frac(primaryUv) - 0.5f;
    float primarySeed = hash21(primaryCell);
    float primaryProfile = pow(saturate(1.0f - length(primaryLocal) * 2.05f), 11.0f + primarySeed * 7.0f);
    float primaryStar = smoothstep(0.9958f, 1.0f, primarySeed) * primaryProfile;

    // Second star layer: smaller, denser stars for extra variation.
    float2 secondaryUv = uv * 320.0f + 37.0f;
    float2 secondaryCell = floor(secondaryUv);
    float2 secondaryLocal = frac(secondaryUv) - 0.5f;
    float secondarySeed = hash21(secondaryCell);
    float secondaryProfile = pow(saturate(1.0f - length(secondaryLocal) * 2.45f), 14.0f + secondarySeed * 10.0f);
    float secondaryStar = smoothstep(0.9978f, 1.0f, secondarySeed) * secondaryProfile;

    // Third star layer: larger, rarer stars to break up the field.
    float2 tertiaryUv = uv * 92.0f - 19.0f;
    float2 tertiaryCell = floor(tertiaryUv);
    float2 tertiaryLocal = frac(tertiaryUv) - 0.5f;
    float tertiarySeed = hash21(tertiaryCell);
    float tertiaryProfile = pow(saturate(1.0f - length(tertiaryLocal) * 1.7f), 7.0f + tertiarySeed * 5.0f);
    float tertiaryStar = smoothstep(0.9915f, 1.0f, tertiarySeed) * tertiaryProfile;

    // Separate twinkle rates keep the star field from pulsing in lockstep.
    float primaryTwinkle = 0.68f + 0.32f * sin(Time * (1.1f + primarySeed * 2.0f) + primarySeed * 19.0f);
    float secondaryTwinkle = 0.75f + 0.25f * sin(Time * (1.7f + secondarySeed * 1.7f) + secondarySeed * 23.0f + 1.2f);
    float tertiaryTwinkle = 0.82f + 0.18f * sin(Time * (0.8f + tertiarySeed * 1.3f) + tertiarySeed * 13.0f + 2.0f);

    // Combine the layers into one night-sky star contribution.
    float3 stars = 0.0f.xxx;
    stars += getStarTint(primarySeed) * primaryStar * (0.45f + primarySeed * 0.7f) * primaryTwinkle;
    stars += getStarTint(secondarySeed * 0.85f + 0.1f) * secondaryStar * (0.25f + secondarySeed * 0.5f) * secondaryTwinkle;
    stars += getStarTint(tertiarySeed * 0.6f + 0.2f) * tertiaryStar * (0.9f + tertiarySeed * 1.4f) * tertiaryTwinkle;
    return stars;
}

// Get sun color for given direction
// The reference shader returns a float here and only draws the bright sun disc.
// This port keeps the helper as float3 so it can add sunset tint and a broader glow without changing call sites.
float3 getSun(float3 dir, float3 sundir)
{
    float sunAmount = pow(max(0.0f, dot(dir, sundir)), 960.0f) * 135.0f;
    float halo = pow(max(0.0f, dot(dir, sundir)), 64.0f) * 0.32f;
    float sunsetAmount = saturate((0.22f - sundir.y) * 3.0f);
    float3 sunColor = lerp(float3(1.0f, 0.97f, 0.92f), float3(1.0f, 0.7f, 0.45f), sunsetAmount);
    return sunColor * (sunAmount + halo);
}

// Get moon color for given direction
float3 getMoon(float3 direction, float3 moonDirection, float nightAmount)
{
    float moonDot = max(0.0f, dot(direction, moonDirection));
    float3 moonUp = abs(moonDirection.y) > 0.98f ? float3(1.0f, 0.0f, 0.0f) : float3(0.0f, 1.0f, 0.0f);
    float3 moonTangent = normalize(cross(moonUp, moonDirection));
    float3 moonBitangent = cross(moonDirection, moonTangent);
    float2 moonPlane = float2(dot(direction, moonTangent), dot(direction, moonBitangent));
    float moonRadius = 0.031f;
    float2 moonUv = moonPlane / moonRadius;
    float moonDistance = length(moonUv);
    float moonDisc = (1.0f - smoothstep(0.9f, 1.0f, moonDistance)) * smoothstep(0.9975f, 0.9994f, moonDot);
    float moonHalo = (smoothstep(0.992f, 0.9985f, moonDot) - moonDisc) * 0.018f;

    float maria = 0.9f + 0.08f * sin(moonUv.x * 7.0f + 0.8f) * sin(moonUv.y * 8.0f - 1.3f);
    float craterBand = 0.96f - 0.05f * sin((moonUv.x + moonUv.y) * 11.0f);
    float surface = maria * craterBand;
    float rimShade = lerp(0.82f, 1.0f, saturate(1.0f - moonDistance * 0.85f));
    float3 moonColor = float3(0.56f, 0.6f, 0.68f);

    return (moonColor * moonDisc * surface * rimShade * 1.35f + moonColor * moonHalo) * nightAmount;
}

// Get atmosphere color for given direction
// The reference shader only returns extra_cheap_atmosphere(dir, getSunDirection()) * 0.5 here.
// This port adds the sun tint/glow helper, the moon, and stars on top of that same atmosphere base.
float3 getAtmosphere(float3 dir)
{
    float3 sundir = getSunDirection();
    float3 moondir = getMoonDirection();
    float nightAmount = getNightAmount(sundir.y);
    float3 stars = getStars(dir) * nightAmount * nightAmount;
    float3 atmosphere = extra_cheap_atmosphere(dir, sundir);
    float3 sun = getSun(dir, sundir);
    float3 moon = getMoon(dir, moondir, nightAmount);
    return atmosphere * 0.5f + sun + moon + stars;
}

// Great tonemapping function from original authors other shader: https://www.shadertoy.com/view/XsGfWV
float3 aces_tonemap(float3 color)
{
    float3x3 m1 = float3x3(
        0.59719f, 0.07600f, 0.02840f,
        0.35458f, 0.90834f, 0.13383f,
        0.04823f, 0.01566f, 0.83777f);
    float3x3 m2 = float3x3(
        1.60475f, -0.10208f, -0.00327f,
        -0.53108f, 1.10813f, -0.07276f,
        -0.07367f, -0.00605f, 1.07602f);

    float3 v = mul(m1, color);
    float3 a = v * (v + 0.0245786f) - 0.000090537f;
    float3 b = v * (0.983729f * v + 0.4329510f) + 0.238081f;
    return pow(clamp(mul(m2, (a / b)), 0.0f, 1.0f), 1.0f / 2.2f);
}

float4 MainPS(VSOutput input) : COLOR0
{
    // get the ray
    float3 ray = getRay(input.TexCoord);

    if (ray.y >= 0.0f)
    {
        // if ray.y is positive, render the sky
        float3 C = getAtmosphere(ray);
        return float4(aces_tonemap(C * 2.0f), 1.0f);
    }

    // now ray.y must be negative, water must be hit
    // define water planes
    float3 waterPlaneHigh = float3(0.0f, 0.0f, 0.0f);
    float3 waterPlaneLow = float3(0.0f, -WATER_DEPTH, 0.0f);

    // define ray origin, moving around
    float3 origin = CameraPosition;

    // calculate intersections and reconstruct positions
    float highPlaneHit = intersectPlane(origin, ray, waterPlaneHigh, float3(0.0f, 1.0f, 0.0f));
    float lowPlaneHit = intersectPlane(origin, ray, waterPlaneLow, float3(0.0f, 1.0f, 0.0f));
    float3 highHitPos = origin + ray * highPlaneHit;
    float3 lowHitPos = origin + ray * lowPlaneHit;

    // raymarch water and reconstruct the hit pos
    float dist = raymarchwater(origin, highHitPos, lowHitPos, WATER_DEPTH);
    float3 waterHitPos = origin + ray * dist;

    // calculate normal at the hit position
    float3 N = normal(waterHitPos.xz, 0.01f, WATER_DEPTH);

    // smooth the normal with distance to avoid disturbing high frequency noise
    N = lerp(N, float3(0.0f, 1.0f, 0.0f), 0.8f * min(1.0f, sqrt(dist * 0.01f) * 1.1f));

    // calculate fresnel coefficient
    float fresnel = 0.04f + (1.0f - 0.04f) * pow(1.0f - max(0.0f, dot(-N, ray)), 5.0f);

    // reflect the ray and make sure it bounces up
    float3 R = normalize(reflect(ray, N));
    R.y = abs(R.y);

    // calculate the reflection and approximate subsurface scattering
    float3 reflection = getAtmosphere(R);
    float3 scattering = float3(0.016f, 0.043f, 0.122f) * 0.085f * (0.2f + (waterHitPos.y + WATER_DEPTH) / WATER_DEPTH);

    // return the combined result
    float3 C = fresnel * reflection + scattering;
    return float4(aces_tonemap(C * 2.0f), 1.0f);
}

technique ProceduralOcean
{
    pass P0
    {
        VertexShader = compile VS_SHADERMODEL MainVS();
        PixelShader = compile PS_SHADERMODEL MainPS();
    }
}
