# 表现层运行时素材来源（A）

- 旧冰碰撞运行时合成占位声已经删除。A 阶段不播放占位音，采样素材和播放器留待 B；未下载第三方录音。
- `Resources/PourNoise.png`：本项目离线生成的 32×128 噪声，固定种子 20261024；原创，按 CC0 1.0 放弃相关权利，无外部来源。
- `RoundedIceMesh`、液体和水柱网格、着色器：本项目原创程序几何及代码；本轮生成的噪声和几何可按 CC0 1.0 使用。没有导入付费资源。
- 霜层、水珠、气泡：本项目程序化着色与基本球网格，无外部图片素材。
- 原有 Art 模型与材质的授权记录仍在原有 LICENSES.md，本轮未改。
- `Art/ShakingIce/` 的 A/B/C FBX 来自用户更新的 `Docs/BarKit_7_2/Models/ShakingIce_*`，原包 `Art_LICENSES.md` 标注原创几何 CC0。`Resources/Ice/` 是这些模型的 Unity 外观预制体及本项目新建 URP 材质；保留透明 IceSurface 与雾状 FrostCore，不覆盖其他美术。


## Q 表现资源

新增许可、逐页 CC0 来源、用户程序纹理和 SIL OFL 字体例外详见 ThirdPartyNotices/audio-and-fonts.md；音频派生记录见 ThirdPartyNotices/provenance/，运行字体派生名 Bar UI SC。


## 日文 UI 字形扩充（2026-11-02）

BarUISubsetSC.ttf 从 NotoSansSC-VF.ttf（SIL OFL 1.1，原字体保留于本地制作资料，完整许可见 ThirdPartyNotices/NotoSansSC-OFL.txt）重新裁剪，保留原有字形、字宽和行高，补入日文界面所需假名与汉字。派生字体名称仍为 Bar UI SC；没有加入新的外部字体或变更许可证。
