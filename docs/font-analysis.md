# Lost Castle 2 font notes

Game path inspected:

`D:\Program Files (x86)\Steam\steamapps\common\Lost Castle 2`

Observed build:

- Unity: `6000.3.16f1`
- Backend: IL2CPP
- Resource system: YooAsset / Addressables
- Current package version: `2026-06-12-760`

Important font bundles:

- `defaultpackage_font_default.bundle` -> `75f5631f1b365e3bda8a4650ed116c54.bundle`
- `defaultpackage_font_tcn.bundle` -> `16b001156a546841d23f91d51dce0314.bundle`
- `defaultpackage_font_jp.bundle` -> `0e7a1edb6b4e8388099e38f542d47add.bundle`
- `defaultpackage_font_kr.bundle` -> `3b254531e8908dcd53ee1ee02ec3e6a9.bundle`

Simplified Chinese main TMP asset:

- Name: `Alibaba-PuHuiTi-Bold SDF`
- Point size: `22`
- Atlas: `4096x2048`
- Padding: `5`
- Glyph count: `4545`
- Character count: `4545`
- Atlas population mode: static
- Multi atlas: off

Other related assets:

- `Alibaba-PuHuiTi-Bold SDF - Chat`: point size `22`, atlas `4096x2048`, padding `5`, dynamic population.
- `Alibaba-PuHuiTi-Bold SDF - BossTitle`: point size `85`, atlas `2048x2048`, padding `5`.
- `SourceHanSansCN-Bold-Hunter SDF-ASCII`: point size `22`, atlas `512x256`, padding `10`.

Material notes:

- `_GradientScale`: `6.0` on Alibaba SDF materials.
- `_OutlineSoftness`: `0.0`
- `_UnderlaySoftness`: `0.0`
- Main default material has `_OutlineWidth` around `0.03`.
- Texture format: Alpha8.
- Texture filter mode: bilinear.
- Mip count: `1`.

Likely cause:

Most CJK glyphs in the main font atlas are only about `20x20` source pixels. SDF remains readable, but small UI text becomes soft after bilinear sampling, outline, and non-integer UI scaling. A runtime high-sampling dynamic TMP font is a safer first mod than rewriting YooAsset bundles.
