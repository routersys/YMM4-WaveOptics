Texture2D InputTexture : register(t0);
Texture2D WeightTexture : register(t1);
SamplerState InputSampler : register(s0);

static const int MaximumRadius = 15;

cbuffer Constants : register(b0)
{
    int radius : packoffset(c0.x);
    int axis : packoffset(c0.y);
    float4 inputBounds : packoffset(c1);
};

float4 SampleInput(float2 uv, float2 scenePosition)
{
    if (scenePosition.x < inputBounds.x || scenePosition.y < inputBounds.y ||
        scenePosition.x >= inputBounds.z || scenePosition.y >= inputBounds.w)
        return (float4)0;
    return InputTexture.SampleLevel(InputSampler, uv, 0);
}

float4 main(
    float4 position : SV_POSITION,
    float4 scenePosition : SCENE_POSITION,
    float4 uv0 : TEXCOORD0,
    float4 uv1 : TEXCOORD1
) : SV_TARGET
{
    int clamped = clamp(radius, 0, MaximumRadius);
    float4 accumulated = (float4)0;

    [loop]
    for (int d = -clamped; d <= clamped; d++)
    {
        float weight = WeightTexture.Load(int3(d + clamped, 0, 0)).r;
        float2 offset = axis == 0 ? float2(d, 0) : float2(0, d);
        accumulated += SampleInput(uv0.xy + offset * uv0.zw, scenePosition.xy + offset) * weight;
    }

    return accumulated;
}
