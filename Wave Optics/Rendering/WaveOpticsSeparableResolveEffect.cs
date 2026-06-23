using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsSeparableResolveEffect(IGraphicsDevicesAndContext devices)
    : D2D1CustomShaderEffectBase(Create<EffectImpl>(devices))
{
    public const int MaximumRank = 4;

    public int Rank
    {
        get => GetIntValue((int)EffectImpl.Properties.Rank);
        set => SetValue((int)EffectImpl.Properties.Rank, value);
    }

    public float Amount
    {
        get => GetFloatValue((int)EffectImpl.Properties.Amount);
        set => SetValue((int)EffectImpl.Properties.Amount, value);
    }

    public float Gain
    {
        get => GetFloatValue((int)EffectImpl.Properties.Gain);
        set => SetValue((int)EffectImpl.Properties.Gain, value);
    }

    public void SetTerm(int index, ID2D1Image? image) => SetInput(index, image, true);
    public void SetSource(ID2D1Image? image) => SetInput(MaximumRank, image, true);

    [CustomEffect(MaximumRank + 1)]
    sealed class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
    {
        ConstantBuffer constants;

        [CustomEffectProperty(PropertyType.Int32, (int)Properties.Rank)]
        public int Rank
        {
            get => constants.Rank;
            set
            {
                constants.Rank = Math.Clamp(value, 0, MaximumRank);
                UpdateConstants();
            }
        }

        [CustomEffectProperty(PropertyType.Float, (int)Properties.Amount)]
        public float Amount
        {
            get => constants.Amount;
            set
            {
                constants.Amount = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
                UpdateConstants();
            }
        }

        [CustomEffectProperty(PropertyType.Float, (int)Properties.Gain)]
        public float Gain
        {
            get => constants.Gain;
            set
            {
                constants.Gain = float.IsFinite(value) ? Math.Clamp(value, 0, 4) : 1;
                UpdateConstants();
            }
        }

        public EffectImpl() : base(ShaderResourceUri.Get("WaveOpticsSeparableResolve"))
        {
            constants.Gain = 1;
        }

        protected override void UpdateConstants()
        {
            drawInformation?.SetPixelShaderConstantBuffer(constants);
        }

        public override void MapInputRectsToOutputRect(RawRect[] inputRects, RawRect[] inputOpaqueSubRects, out RawRect outputRect, out RawRect outputOpaqueSubRect)
        {
            inputRect = inputRects[0];
            var source = inputRects[MaximumRank];
            constants.InputBounds = new Vector4(source.Left, source.Top, source.Right, source.Bottom);
            UpdateConstants();
            outputRect = inputRects[0];
            outputOpaqueSubRect = default;
        }

        public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
        {
            for (var index = 0; index < inputRects.Length; index++)
                inputRects[index] = outputRect;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ConstantBuffer
        {
            public int Rank;
            public float Amount;
            public float Gain;
            public float Padding;
            public Vector4 InputBounds;
        }

        public enum Properties
        {
            Rank,
            Amount,
            Gain,
        }
    }
}
