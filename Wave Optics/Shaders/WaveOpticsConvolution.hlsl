Texture2D InputTexture : register(t0);
Texture2D KernelTexture : register(t1);
SamplerState InputSampler : register(s0);

static const int MaximumRadius = 15;

cbuffer Constants : register(b0)
{
    int kernelSize : packoffset(c0.x);
    float amount : packoffset(c0.y);
    float gain : packoffset(c0.z);
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
    float4 source = SampleInput(uv0.xy, scenePosition.xy);
    if (amount <= 0.0f || kernelSize <= 1)
        return source;

    int radius = clamp(kernelSize / 2, 0, MaximumRadius);
    float4 convolved = (float4)0;

    [loop]
    for (int y = -MaximumRadius; y <= MaximumRadius; y++)
    {
        if (abs(y) > radius)
            continue;
        [loop]
        for (int x = -MaximumRadius; x <= MaximumRadius; x++)
        {
            if (abs(x) > radius)
                continue;
            float weight = KernelTexture.Load(int3(x + radius, y + radius, 0)).r;
            float2 offset = float2(x, y);
            convolved += SampleInput(uv0.xy + offset * uv0.zw, scenePosition.xy + offset) * weight;
        }
    }

    convolved *= gain;
    return lerp(source, convolved, amount);
}
