Texture2D Term0 : register(t0);
Texture2D Term1 : register(t1);
Texture2D Term2 : register(t2);
Texture2D Term3 : register(t3);
Texture2D SourceTexture : register(t4);
SamplerState InputSampler : register(s0);

cbuffer Constants : register(b0)
{
    int rank : packoffset(c0.x);
    float amount : packoffset(c0.y);
    float gain : packoffset(c0.z);
    float4 inputBounds : packoffset(c1);
};

float4 main(
    float4 position : SV_POSITION,
    float4 scenePosition : SCENE_POSITION,
    float4 uv0 : TEXCOORD0,
    float4 uv1 : TEXCOORD1,
    float4 uv2 : TEXCOORD2,
    float4 uv3 : TEXCOORD3,
    float4 uv4 : TEXCOORD4
) : SV_TARGET
{
    float4 source = (float4)0;
    if (scenePosition.x >= inputBounds.x && scenePosition.y >= inputBounds.y &&
        scenePosition.x < inputBounds.z && scenePosition.y < inputBounds.w)
        source = SourceTexture.SampleLevel(InputSampler, uv4.xy, 0);

    if (amount <= 0.0f || rank <= 0)
        return source;

    float4 convolved = Term0.SampleLevel(InputSampler, uv0.xy, 0);
    if (rank > 1)
        convolved += Term1.SampleLevel(InputSampler, uv1.xy, 0);
    if (rank > 2)
        convolved += Term2.SampleLevel(InputSampler, uv2.xy, 0);
    if (rank > 3)
        convolved += Term3.SampleLevel(InputSampler, uv3.xy, 0);

    convolved *= gain;
    return lerp(source, convolved, amount);
}
