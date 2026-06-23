# v1.0.0 - 波動光学 for YMM4

YukkuriMovieMaker4 向けの波動光学エフェクトプラグインの初回リリースです。
フラウンホーファー回折理論に基づいて点像分布関数（PSF）を計算し、Direct2D カスタムシェーダーで
映像を畳み込む映像エフェクトプラグインです。
波長・F値・画素ピッチ・開口形状・ゼルニケ波面収差係数をパラメータとして持ち、
円形開口・正多角形開口・中央遮蔽・6 種類の波面収差項に対応します。
8 言語対応 UI を備えます。

---

## 新機能

### 1. 公開コントラクト（WaveOptics.Abstractions）

`WaveOptics.Abstractions` は主プラグインから独立したライブラリとして提供されます。

#### ApertureShape

開口形状を表す列挙体です。

| 値 | 意味 |
|---|---|
| `Circular` | 円形開口 |
| `RegularPolygon` | 正多角形開口 |

#### OpticalApiVersion

`readonly record struct OpticalApiVersion(int Major, int Minor)` で宣言されます。
`Current` 静的プロパティは `new(1, 0)` を返却します。

#### OpticalCapabilities

PSF 生成器の対応機能を表す `[Flags]` 列挙体です。

| 値 | 意味 |
|---|---|
| `None` | 0 |
| `MonochromaticPsf` | 単色 PSF 生成 |
| `CircularAperture` | 円形開口 |
| `RegularPolygonAperture` | 正多角形開口 |
| `CentralObstruction` | 中央遮蔽 |
| `ZernikeAberration` | ゼルニケ収差 |
| `DirectConvolution` | 直接畳み込み |

#### WavefrontAberration

8 つの収差係数を保持する `readonly struct` で、`IEquatable<WavefrontAberration>` を実装します。

| フィールド | 説明 |
|---|---|
| `DefocusWaves` | デフォーカス係数（-10〜10 waves） |
| `AstigmatismVerticalWaves` | 縦横非点収差係数 |
| `AstigmatismObliqueWaves` | 斜め非点収差係数 |
| `ComaHorizontalWaves` | 水平コマ収差係数 |
| `ComaVerticalWaves` | 垂直コマ収差係数 |
| `TrefoilHorizontalWaves` | 水平トレフォイル係数 |
| `TrefoilVerticalWaves` | 垂直トレフォイル係数 |
| `SphericalWaves` | 球面収差係数 |

コンストラクターは各係数を `ValidateFiniteRange(-10, 10)` で検証します。
`==`・`!=` 演算子および `GetHashCode` を実装します。

#### PsfDescriptor

PSF 計算パラメータを保持する `sealed` クラスで、`IEquatable<PsfDescriptor>` を実装します。

コンストラクターが受け取るパラメータと検証条件は以下のとおりです。

| パラメータ | 型 | 検証条件 |
|---|---|---|
| `pupilGridSize` | `int` | 64〜2048、かつ 2 の冪 |
| `pupilDiameterSamples` | `int` | 16〜`pupilGridSize / 2` |
| `kernelSize` | `int` | 3〜255、かつ奇数 |
| `wavelengthNanometers` | `double` | 有限値、380〜780 |
| `fNumber` | `double` | 有限値、0.5〜64 |
| `sensorPixelPitchMicrometers` | `double` | 有限値、0.25〜100 |
| `apertureShape` | `ApertureShape` | 定義済み列挙値 |
| `bladeCount` | `int` | 3〜32 |
| `bladeRotationDegrees` | `double` | 有限値、-360〜360 |
| `centralObstructionRatio` | `double` | 有限値、0〜0.95 |
| `aberration` | `WavefrontAberration` | — |

`Equals` はすべてのフィールドの完全一致で判定します。`GetHashCode` は `HashCode.Combine` を 2 段に分けて計算します。

#### PsfKernel

PSF カーネルデータを保持する `sealed` クラスです。

コンストラクターは `size`（奇数正整数）と `values`（`size × size` 長、全要素非負有限）を受け取り、
合計値が正であることを検証してから値の総和で正規化します。入力配列は内部にコピーします。

`Sum` メソッドはカハン補償加算でエラー累積を抑えた合計値を返却します。

| メンバー | 説明 |
|---|---|
| `Size` | カーネルの一辺の画素数 |
| `Values` | 正規化済み値の `ReadOnlyMemory<double>` |
| `this[int x, int y]` | 範囲検証付きインデクサー |
| `ToArray()` | 内部配列のクローン |

#### PsfDiagnostics

`readonly record struct PsfDiagnostics` で以下の診断値を保持します。

| フィールド | 説明 |
|---|---|
| `OpenPupilSampleCount` | 開口内の有効サンプル数 |
| `FocalPlaneSamplePitchMicrometers` | 焦点面サンプル間隔（μm） |
| `UnnormalizedKernelEnergy` | 正規化前のカーネルエネルギー |
| `PeakIntensity` | カーネルの最大強度値 |

#### PsfGenerationResult

`sealed record PsfGenerationResult(PsfKernel Kernel, PsfDiagnostics Diagnostics)` で宣言されます。

#### IPsfGenerator

```csharp
public interface IPsfGenerator
{
    OpticalApiVersion ApiVersion { get; }
    OpticalCapabilities Capabilities { get; }
    PsfGenerationResult Generate(PsfDescriptor descriptor);
}
```

---

### 2. 高速フーリエ変換（FastFourierTransform）

`internal static class FastFourierTransform` は 1 次元および 2 次元の FFT・逆 FFT を提供します。

| メソッド | 説明 |
|---|---|
| `Forward(Span<Complex>)` | 1 次元前進 FFT |
| `Inverse(Span<Complex>)` | 1 次元逆 FFT（1/N スケール適用） |
| `Forward2D(Complex[], int, int)` | 2 次元前進 FFT（行→列の順） |
| `Inverse2D(Complex[], int, int)` | 2 次元逆 FFT |

1 次元実装はクーリー・テューキーアルゴリズムに基づく入力サイズ 2 の冪専用の再帰なし実装です。ビット反転置換を前処理として行い、以降を長さ倍増ループで処理します。逆変換では最後に `1/count` を乗算します。

2 次元実装は各行・各列に独立して 1 次元変換を適用します。列方向の処理には作業用配列 `new Complex[height]` を確保します。

---

### 3. ゼルニケ波面評価（ZernikeWavefront）

`internal static class ZernikeWavefront` は正規化ゼルニケ多項式による波面位相を評価します。

`Evaluate(double x, double y, WavefrontAberration aberration)` は以下の項の線形結合を返却します。

| 項 | 式 |
|---|---|
| デフォーカス | `√3 × (2ρ² − 1)` |
| 縦横非点収差 | `√6 × (x² − y²)` |
| 斜め非点収差 | `2√6 × xy` |
| 水平コマ収差 | `√8 × (3ρ² − 2) × x` |
| 垂直コマ収差 | `√8 × (3ρ² − 2) × y` |
| 水平トレフォイル | `√8 × x × (x² − 3y²)` |
| 垂直トレフォイル | `√8 × y × (3x² − y²)` |
| 球面収差 | `√5 × (6ρ⁴ − 6ρ² + 1)` |

`WavefrontAberration` の `TrefoilHorizontalWaves`・`TrefoilVerticalWaves` は`WaveOpticsEffect` の UI には公開されておらず、常に 0 として扱われます。

---

### 4. フラウンホーファーPSF生成器（FraunhoferPsfGenerator）

`public sealed class FraunhoferPsfGenerator : IPsfGenerator` を実装します。

`Capabilities` は以下のフラグを返却します。
`MonochromaticPsf | CircularAperture | RegularPolygonAperture | CentralObstruction | ZernikeAberration | DirectConvolution`

#### Generate メソッド

1. `gridSize × gridSize` の `Complex[]` 配列を確保します。
2. グリッド中心を基準に各サンプル `(x, y)` を正規化座標 `(normalizedX, normalizedY)` に変換し、
   `IsInsideAperture` で開口内を判定します。
3. 開口内のサンプルに対して `ZernikeWavefront.Evaluate` で波面位相 `φ` を計算し、
   `Complex.FromPolarCoordinates(1d, 2π × φ)` を瞳関数値として設定します。
4. `FastFourierTransform.Forward2D` を適用します。
5. 変換後の値の二乗絶対値（強度）を計算し、FFT のゼロ周波数が中心に来るよう DC シフトして
   `double[]` 配列に格納します。全強度の和で正規化します。
6. 焦点面サンプルピッチ `λ × F × D / N`（μm）を計算し、カーネルの各画素に対応する
   焦点面座標を求めて `SampleBilinear` で強度を取得します。
7. `new PsfKernel(kernelSize, kernel)` を生成して返却します。

#### IsInsideAperture メソッド

- 正規化半径の二乗が 1 超 → 開口外。
- 正規化半径の二乗が `obstructionRatio²` 未満 → 遮蔽領域。
- 円形開口の場合は上記を満たせば開口内。
- 正多角形の場合：`Math.Atan2(y, x) - rotation` を `2π / bladeCount` で折り畳み、境界距離
  `cos(π / bladeCount) / cos(folded)` と半径を比較します。

#### SampleBilinear メソッド

双線形補間でグリッド外を 0 として返却します。

---

### 5. PSFビットマップ生成（PsfBitmapFactory）

`internal static class PsfBitmapFactory` の `Create(ID2D1DeviceContext, PsfKernel)` は
PSF カーネルの各値を RGBA 4 チャンネルの `float[]` に展開（R=G=B=値, A=1）し、
ピン留めポインタを使って `ID2D1DeviceContext1.CreateBitmap` を呼び出します。
ピクセルフォーマットは `Format.R32G32B32A32_Float` / `AlphaMode.Ignore` / 96 DPI です。

---

### 6. カスタムシェーダーエフェクト（WaveOpticsConvolutionEffect）

`internal sealed class WaveOpticsConvolutionEffect : D2D1CustomShaderEffectBase` は
`[CustomEffect(2)]` の 2 入力エフェクトとして宣言されます（入力 0: ソース画像、入力 1: PSF ビットマップ）。

定数は以下のとおりです。

| 定数 | 値 |
|---|---|
| `MaximumKernelSize` | `31` |

公開プロパティは `GetIntValue`・`GetFloatValue`・`SetValue` を介して `EffectImpl` へ転送します。

| プロパティ | 型 | 説明 |
|---|---|---|
| `KernelSize` | `int` | カーネルサイズ（奇数にクランプ、最大 31） |
| `Amount` | `float` | 適用量（0〜1） |
| `Gain` | `float` | 利得（0〜4） |

`SetSource(ID2D1Image?)` は入力 0 を設定します。`SetKernel(ID2D1Image?)` は入力 1 を設定します。

#### EffectImpl（内部 sealed クラス）

`ConstantBuffer` 構造体（`LayoutKind.Sequential`）のレイアウトは以下のとおりです。

| フィールド | 型 | 説明 |
|---|---|---|
| `KernelSize` | `int` | カーネルサイズ |
| `Amount` | `float` | 適用量 |
| `Gain` | `float` | 利得 |
| `Padding` | `float` | アライメント用 |
| `InputBounds` | `Vector4` | 入力矩形（Left, Top, Right, Bottom） |

コンストラクターは `constants.KernelSize = 1`・`constants.Gain = 1` を初期値として設定します。

`MapInputRectsToOutputRect` は `inputRect` と `inputBounds` を更新した後、
`Inflate(inputRect, GetRadius())` を出力矩形に設定します。

`GetRadius` は `Amount > 0` かつ `KernelSize > 1` のとき `KernelSize / 2` を、それ以外は `0` を返却します。

#### HLSLシェーダー（WaveOpticsConvolution.hlsl）

`t0`（ソース画像）・`t1`（PSF ビットマップ）・`b0`（定数バッファー）を持つピクセルシェーダーです。

- `amount <= 0` または `kernelSize <= 1` の場合はソースをそのまま返却します。
- ループ範囲は固定 `-MaximumRadius`〜`MaximumRadius`（= -15〜15）で、
  `abs(x) > radius` または `abs(y) > radius` のサンプルは `continue` でスキップします。
- PSF の重みは `KernelTexture.Load(int3(x + radius, y + radius, 0)).r` で取得します。
- 入力矩形外のサンプルは黒として扱います。
- `lerp(source, convolved * gain, amount)` で出力します。

---

### 7. エフェクト定義（WaveOpticsEffect）

`public sealed class WaveOpticsEffect : VideoEffectBase` を継承します。

`[VideoEffect]` 属性は以下のパラメーターで宣言されます。

- 表示名：`Texts.EffectName`（ローカライズキー）
- カテゴリー：`VideoEffectCategories.Filtering`
- 検索タグ：`TagDiffraction`・`TagLens`・`TagPsf`・`TagOptics`（「回折」・「レンズ」・「点像分布関数」・「光学」）
- `IsAviUtlSupported = false` により AviUtl 向け EXO 出力は非対応
- `ResourceType = typeof(Texts)` でローカライズリソースを指定

`Label` プロパティは `Texts.EffectName` を返却します。

公開プロパティは以下のとおりです。

**出力グループ**

| プロパティ | 型 | デフォルト | 内部範囲 |
|---|---|---|---|
| `Amount` | `Animation` | 100 | 0〜100 |
| `Gain` | `Animation` | 100 | 0〜400 |

**光学系グループ**

| プロパティ | 型 | デフォルト | 内部範囲 |
|---|---|---|---|
| `Wavelength` | `Animation` | 550 | 380〜780 |
| `FNumber` | `Animation` | 8 | 0.5〜64 |
| `PixelPitch` | `Animation` | 4 | 0.25〜100 |
| `KernelRadius` | `int` | 15 | 1〜15 |
| `Quality` | `WaveOpticsQuality` | `Standard` | — |

**開口グループ**

| プロパティ | 型 | デフォルト | 内部範囲 |
|---|---|---|---|
| `ApertureShape` | `WaveOpticsApertureShape` | `Circular` | — |
| `BladeCount` | `int` | 6 | 3〜32 |
| `BladeRotation` | `Animation` | 0 | -360〜360 |
| `Obstruction` | `Animation` | 0 | 0〜95 |

**波面収差グループ**

| プロパティ | 型 | デフォルト | 内部範囲 |
|---|---|---|---|
| `Defocus` | `Animation` | 0 | -10〜10 |
| `AstigmatismVertical` | `Animation` | 0 | -10〜10 |
| `AstigmatismOblique` | `Animation` | 0 | -10〜10 |
| `ComaHorizontal` | `Animation` | 0 | -10〜10 |
| `ComaVertical` | `Animation` | 0 | -10〜10 |
| `Spherical` | `Animation` | 0 | -10〜10 |

`GetAnimatables` は `Amount`・`Gain`・`Wavelength`・`FNumber`・`PixelPitch`・`BladeRotation`・
`Obstruction`・`Defocus`・`AstigmatismVertical`・`AstigmatismOblique`・`ComaHorizontal`・
`ComaVertical`・`Spherical` を yield します。

---

### 8. エフェクトプロセッサー（WaveOpticsEffectProcessor）

`internal sealed class WaveOpticsEffectProcessor : VideoEffectProcessorBase` を継承します。

#### Update メソッド

`IsPassThroughEffect || effect is null` の場合は `effectDescription.DrawDescription` をそのまま返却します。

各フレームで以下の値を計算します。

| パラメーター | クランプ範囲 | フォールバック |
|---|---|---|
| `amount` | 0〜1 | 0 |
| `gain` | 0〜4 | 1 |
| `wavelength` | 380〜780 | 550 |
| `fNumber` | 0.5〜64 | 8 |
| `pixelPitch` | 0.25〜100 | 4 |
| `obstruction / 100` | 0〜0.95 | 0 |
| 各収差係数 | -10〜10 | 0 |

`Parameters` レコードとして前フレームの値と比較し、変化があった場合のみ `UpdateKernel` を呼び出します。
`Amount`・`Gain` も同様に差分検出して `effect` のプロパティを更新します。

#### UpdateKernel メソッド

`WavefrontAberration` と `PsfDescriptor` を構築して `FraunhoferPsfGenerator.Generate` を呼び出します。

品質から FFT 格子サイズへの変換は以下のとおりです。

| `WaveOpticsQuality` | FFT 格子サイズ |
|---|---|
| `Draft` | 128 |
| `High` | 512 |
| その他（`Standard`） | 256 |

`PsfDescriptor` の `pupilDiameterSamples` は `gridSize / 4` に固定されます。
`kernelSize` は `KernelRadius * 2 + 1` です。

`PsfBitmapFactory.Create` で新しいビットマップを作成し、`disposer.RemoveAndDispose` で旧ビットマップを破棄してから新ビットマップを `effect` に設定します。

#### CreateEffect / setInput / ClearEffectChain

`CreateEffect` は `WaveOpticsConvolutionEffect` を生成し、`IsEnabled` が false の場合は破棄して
`null` を返却します。有効な場合は `disposer` に収集し、`Output` を取得して返却します。

`setInput` は `effect?.SetSource(input)` を呼び出します。

`ClearEffectChain` は `effect?.SetSource(null)`・`effect?.SetKernel(null)` を呼び出し、
`currentParameters = null`・`isFirst = true` にリセットします。

---

### 9. 公開API（WaveOpticsApi）

`public static class WaveOpticsApi` を提供します。

| メンバー | 説明 |
|---|---|
| `Version` | `OpticalApiVersion.Current` を返却します |
| `CreatePsfGenerator()` | `new FraunhoferPsfGenerator()` を返却します |

---

### 10. ローカライズ（Texts）

`Texts` クラスは `[AutoGenLocalizer]` 属性を持つ `partial` クラスとして宣言されます。
`YukkuriMovieMaker.Generator` のソースジェネレーターが `Texts.csv` を処理し、
各ロケールのリソースファイルを自動生成します。

対応言語：日本語（`ja-jp`）・英語（`en-us`）・中国語簡体字（`zh-cn`）・中国語繁体字（`zh-tw`）・
韓国語（`ko-kr`）・スペイン語（`es-es`）・アラビア語（`ar-sa`）・インドネシア語（`id-id`）

ローカライズキーの一覧は以下のとおりです。

| キー | ja-jp |
|---|---|
| `EffectName` | 波動光学 |
| `OutputGroup` | 出力 |
| `OpticsGroup` | 光学系 |
| `ApertureGroup` | 開口 |
| `AberrationGroup` | 波面収差 |
| `AmountName` | 適用量 |
| `GainName` | 利得 |
| `WavelengthName` | 波長 |
| `FNumberName` | F値 |
| `PixelPitchName` | 画素ピッチ |
| `KernelRadiusName` | カーネル半径 |
| `QualityName` | 品質 |
| `QualityDraft` | 高速 |
| `QualityStandard` | 標準 |
| `QualityHigh` | 高精度 |
| `ApertureShapeName` | 開口形状 |
| `CircularAperture` | 円形 |
| `RegularPolygonAperture` | 正多角形 |
| `BladeCountName` | 羽根枚数 |
| `BladeRotationName` | 開口回転 |
| `ObstructionName` | 中央遮蔽 |
| `DefocusName` | デフォーカス |
| `AstigmatismVerticalName` | 縦横非点収差 |
| `AstigmatismObliqueName` | 斜め非点収差 |
| `ComaHorizontalName` | 水平コマ収差 |
| `ComaVerticalName` | 垂直コマ収差 |
| `SphericalName` | 球面収差 |
| `TagDiffraction` | 回折 |
| `TagLens` | レンズ |
| `TagPsf` | 点像分布関数 |
| `TagOptics` | 光学 |
