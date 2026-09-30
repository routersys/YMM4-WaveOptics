using System.Runtime.InteropServices;
using Vortice;
using Vortice.Direct2D1;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Player.Video;

namespace WaveOptics.Rendering;

internal sealed class WaveOpticsCustomEffect(IGraphicsDevicesAndContext devices)
    : D2D1CustomShaderEffectBase(Create<EffectImpl>(devices))
{
    public float Amount { set => SetValue((int)EffectImpl.Properties.Amount, value); }

    [CustomEffect(2)]
    private sealed class EffectImpl : D2D1CustomShaderEffectImplBase<EffectImpl>
    {
        private ConstantBuffer _cb;

        [CustomEffectProperty(PropertyType.Float, (int)Properties.Amount)]
        public float Amount
        {
            get => _cb.Amount;
            set
            {
                _cb.Amount = Math.Clamp(value, 0f, 1f);
                UpdateConstants();
            }
        }

        public EffectImpl() : base(ShaderResourceUri.Get("WaveOptics"))
        {
        }

        protected override void UpdateConstants()
        {
            if (drawInformation is null)
                return;

            drawInformation.SetPixelShaderConstantBuffer(_cb);
        }

        public override void MapInputRectsToOutputRect(
            RawRect[] inputRects,
            RawRect[] inputOpaqueSubRects,
            out RawRect outputRect,
            out RawRect outputOpaqueSubRect)
        {
            if (inputRects.Length == 0)
            {
                outputRect = default;
                outputOpaqueSubRect = default;
                return;
            }

            var union = inputRects[0];
            var inputCount = _cb.Amount <= 0f ? 1 : inputRects.Length;
            for (var i = 1; i < inputCount; i++)
            {
                var rect = inputRects[i];
                union = new RawRect(
                    Math.Min(union.Left, rect.Left),
                    Math.Min(union.Top, rect.Top),
                    Math.Max(union.Right, rect.Right),
                    Math.Max(union.Bottom, rect.Bottom));
            }
            outputRect = union;
            outputOpaqueSubRect = default;
        }

        public override void MapOutputRectToInputRects(RawRect outputRect, RawRect[] inputRects)
        {
            for (var i = 0; i < inputRects.Length; i++)
                inputRects[i] = outputRect;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ConstantBuffer
        {
            public float Amount;
            public float Pad0;
            public float Pad1;
            public float Pad2;
        }

        public enum Properties
        {
            Amount = 0,
        }
    }
}
