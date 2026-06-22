using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsConvolutionEffect(IGraphicsDevicesAndContext devices)
    : D2D1CustomShaderEffectBase(Create<EffectImpl>(devices))
{
    public const int MaximumKernelSize = 31;

    public int KernelSize
    {
        get => GetIntValue((int)EffectImpl.Properties.KernelSize);
        set => SetValue((int)EffectImpl.Properties.KernelSize, value);
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

    public void SetSource(ID2D1Image? image) => SetInput(0, image, true);
    public void SetKernel(ID2D1Image? image) => SetInput(1, image, true);

    [CustomEffect(2)]
    sealed class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
    {
        ConstantBuffer constants;
        RawRect kernelRect;

        [CustomEffectProperty(PropertyType.Int32, (int)Properties.KernelSize)]
        public int KernelSize
        {
            get => constants.KernelSize;
            set
            {
                var clamped = Math.Clamp(value, 1, MaximumKernelSize);
                constants.KernelSize = (clamped & 1) == 0 ? clamped - 1 : clamped;
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

        public EffectImpl() : base(ShaderResourceUri.Get("WaveOpticsConvolution"))
        {
            constants.KernelSize = 1;
            constants.Gain = 1;
        }

        protected override void UpdateConstants()
        {
            drawInformation?.SetPixelShaderConstantBuffer(constants);
        }

        public override void MapInputRectsToOutputRect(RawRect[] inputRects, RawRect[] inputOpaqueSubRects, out RawRect outputRect, out RawRect outputOpaqueSubRect)
        {
            inputRect = inputRects[0];
            kernelRect = inputRects[1];
            constants.InputBounds = new Vector4(inputRect.Left, inputRect.Top, inputRect.Right, inputRect.Bottom);
            UpdateConstants();
            outputRect = Inflate(inputRect, GetRadius());
            outputOpaqueSubRect = default;
        }

        public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
        {
            inputRects[0] = Inflate(outputRect, GetRadius());
            inputRects[1] = kernelRect;
        }

        int GetRadius() => constants.Amount > 0 ? constants.KernelSize / 2 : 0;

        static RawRect Inflate(RawRect rect, int radius)
        {
            return new RawRect(
                Saturate((long)rect.Left - radius),
                Saturate((long)rect.Top - radius),
                Saturate((long)rect.Right + radius),
                Saturate((long)rect.Bottom + radius));
        }

        static int Saturate(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

        [StructLayout(LayoutKind.Sequential)]
        struct ConstantBuffer
        {
            public int KernelSize;
            public float Amount;
            public float Gain;
            public float Padding;
            public Vector4 InputBounds;
        }

        public enum Properties
        {
            KernelSize,
            Amount,
            Gain,
        }
    }
}
