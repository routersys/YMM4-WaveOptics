using System.Numerics;
using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsSeparablePassEffect(IGraphicsDevicesAndContext devices)
    : D2D1CustomShaderEffectBase(Create<EffectImpl>(devices))
{
    public const int MaximumRadius = 15;

    public int Radius
    {
        get => GetIntValue((int)EffectImpl.Properties.Radius);
        set => SetValue((int)EffectImpl.Properties.Radius, value);
    }

    public int Axis
    {
        get => GetIntValue((int)EffectImpl.Properties.Axis);
        set => SetValue((int)EffectImpl.Properties.Axis, value);
    }

    public void SetSource(ID2D1Image? image) => SetInput(0, image, true);
    public void SetWeights(ID2D1Image? image) => SetInput(1, image, true);

    [CustomEffect(2)]
    sealed class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
    {
        ConstantBuffer constants;
        RawRect weightRect;

        [CustomEffectProperty(PropertyType.Int32, (int)Properties.Radius)]
        public int Radius
        {
            get => constants.Radius;
            set
            {
                constants.Radius = Math.Clamp(value, 0, MaximumRadius);
                UpdateConstants();
            }
        }

        [CustomEffectProperty(PropertyType.Int32, (int)Properties.Axis)]
        public int Axis
        {
            get => constants.Axis;
            set
            {
                constants.Axis = value != 0 ? 1 : 0;
                UpdateConstants();
            }
        }

        public EffectImpl() : base(ShaderResourceUri.Get("WaveOpticsSeparablePass"))
        {
        }

        protected override void UpdateConstants()
        {
            drawInformation?.SetPixelShaderConstantBuffer(constants);
            drawInformation?.SetOutputBuffer(BufferPrecision.PerChannel32Float, ChannelDepth.Four);
        }

        public override void MapInputRectsToOutputRect(RawRect[] inputRects, RawRect[] inputOpaqueSubRects, out RawRect outputRect, out RawRect outputOpaqueSubRect)
        {
            inputRect = inputRects[0];
            weightRect = inputRects[1];
            constants.InputBounds = new Vector4(inputRect.Left, inputRect.Top, inputRect.Right, inputRect.Bottom);
            UpdateConstants();
            outputRect = Inflate(inputRect);
            outputOpaqueSubRect = default;
        }

        public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
        {
            inputRects[0] = Inflate(outputRect);
            inputRects[1] = weightRect;
        }

        RawRect Inflate(RawRect rect)
        {
            var horizontal = constants.Axis == 0 ? constants.Radius : 0;
            var vertical = constants.Axis == 0 ? 0 : constants.Radius;
            return new RawRect(
                Saturate((long)rect.Left - horizontal),
                Saturate((long)rect.Top - vertical),
                Saturate((long)rect.Right + horizontal),
                Saturate((long)rect.Bottom + vertical));
        }

        static int Saturate(long value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);

        [StructLayout(LayoutKind.Sequential)]
        struct ConstantBuffer
        {
            public int Radius;
            public int Axis;
            public float Padding0;
            public float Padding1;
            public Vector4 InputBounds;
        }

        public enum Properties
        {
            Radius,
            Axis,
        }
    }
}
