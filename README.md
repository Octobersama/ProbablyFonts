# ProbablyFonts

为《深空当铺：可能是偷的》（Probably Stolen）补充跨语言的 TextMesh Pro 字体回退。使用游戏已有字体，让中文、日文等文字不再依赖当前游戏语言才有机会显示，同时尽量保留正文、标题、手写和科幻字体各自的风格。

ProbablyFonts 是独立模组，不依赖 ProbablyEdited，也不包含游戏字体或游戏程序集。

## 安装与停用

1. 将构建得到的 `dist/ProbablyFonts/` 整个文件夹复制到游戏的 `Mods/` 中。
2. 在游戏模组列表中启用 **ProbablyFonts**，然后重启游戏。
3. 在游戏原有语言菜单中切换语言即可；本模组没有额外语言选项，不修改翻译或游戏语言设置。
4. 停用时在模组列表中关闭并重启。模组不保存存档数据或偏好；停用会移除自己的回退项，并释放自己创建的字体和材质。

**首次使用建议先停用 Chinese Font Fix／中文字体补丁等其他全局字体模组。** 多个模组同时更改全局回退、字形选择或 `FontLoader` 字体时，结果取决于它们的执行顺序。ProbablyFonts 不会自动关闭其他模组；需要对比时一次只启用一个字体模组。

## 字体选择

- 保留文本原有的主字体、字号、样式和富文本语义，在全局回退表中补充游戏已有字体的独立副本；不覆盖其他模组已有回退项的顺序。
- 对同一段文字的 CJK 字符组，优先选择能覆盖整组字符的原生角色字体。角色字体只有部分字形时，尝试用一个完整的原生脚本字体覆盖整组，避免同一词中交替使用粗细不同的字体。
- 有假名时选择日文字形，有谚文时选择韩文字形，注音符号使用中文字形。混合文字中的汉字跟随数量更多的假名／谚文；数量相同或只有汉字时，固定选择中文，不随游戏语言来回切换。**只有汉字的日文无法据此辨别，可能显示中文区域字形。** 这不向游戏或 ProbablyEdited 的语言菜单添加任何语言。
- 保留原生角色对应的手写、正文和显示字体。CJK 副本的材质字重以该角色为基准，处理粗体请求时不复用错误的字重材质；不同字族本身的轮廓和视觉粗细仍可能存在差别。
- 原生静态字库覆盖不足时，仅在游戏字体仍有可用源字体的情况下，为当前文字需要的字符创建动态字形；不预先生成整个 Unicode 字符集，不修改游戏字体资产。

如果游戏提供的字体仍不能完整覆盖某个字符组，模组会记录警告，并让该组继续使用 TMP 原来的回退路径。它不会下载字体、替换文字或伪造字形。

模组在自己的资源加载完成事件之后等待字体和本地化就绪，场景／语言变化时重新协调回退项。已有文本会刷新，新创建的控件、下拉菜单和后续文本修改走同一个选字路径；没有每帧扫描整个场景。

## 构建

需要 .NET SDK 和本机安装的游戏。模组目标为 `netstandard2.1`，纯逻辑检查使用 .NET 10；不下载额外测试框架。

```powershell
dotnet build ProbablyFonts.csproj -c Release
dotnet run --project tests/ProbablyFonts.PureChecks -c Release
```

游戏不在默认位置时：

```powershell
dotnet build ProbablyFonts.csproj -c Release -p:GameDirectory="D:\SteamLibrary\steamapps\common\Probably Stolen"
```

构建自动生成 `dist/ProbablyFonts/`，只包含 `ProbablyFonts.dll`、`manifest.xml`、`LICENSE`、`NOTICE`。游戏程序集仅作为编译引用，不会被复制进安装包。源码、构建和检查均不引用父目录，可以把整个项目目录移到其他位置独立管理。

## 兼容性与实现边界

- 静态方法体确认，`ScriptFontSwap` 的中日文字体选择直接使用 `FontLoader` 字体字段，不要求先切换游戏语言；但 PE 元数据不能证明英文冷启动时的序列化字体引用和字库完整度。首次验收应直接以英语冷启动，不要先切换到中文来预加载字体。
- manifest 声明 `0.46D` 和 `1.0.0`：前者对应本机 `100-Rev13` 客户端的 `Application.version`，后者对应官方为 `100-Rev14` 说明的 `1.0.0.*` 版本前缀。版本声明不能代替对应客户端的运行验证。
- 使用游戏加载器应用 Harmony 补丁，不另建 Harmony 实例或卸载其他模组的补丁。
- 补丁目标是明确列出的 TMP 字形选择、输入缓冲及材质方法。输入快照覆盖字符串、字符数组、`StringBuilder` 和数字格式化输入，不在每个字形查询时重新读取整段文本。
- 当前官方扫描规则豁免字面量指向 `TMPro`／Unity UI 类型的 Harmony 目标；manifest 不申请额外能力。模组不联网、不访问用户文件、不加载外部字体。
- TMP 内部方法或游戏的字体结构更新后可能需要适配。官方指南将修改现有内置 UI 列为不受支持的扩展，并要求作者自行承担 Harmony 补丁兼容性。发布或升级前应在目标游戏版本中检查 `mod-scan probably_fonts` 结果和实际界面。
- 静态检查与纯逻辑测试不能验证实际字形、材质渲染、缺字和光学字重；首次安装后应检查不同游戏语言下的中日文混排、下拉菜单、正文和标题。

## 参考资料

查阅日期：2026-11-12。

- Probably Stolen [Modding Guide](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md)：[资源就绪](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md#knowing-when-your-assets-are-ready)、[Harmony 与 TMP/UI 扫描规则](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md#harmony-patches)、[支持范围](https://github.com/dragonpan2/probably-stolen-modding/blob/main/ModdingGuide.md#supported--unsupported-modding-features)。
- Probably Stolen [APIChanges](https://raw.githubusercontent.com/dragonpan2/probably-stolen-modding/main/APIChanges.md)、[Code of Conduct](https://github.com/dragonpan2/probably-stolen-modding/blob/main/CodeOfConduct.md)。
- Unity TMP：[回退链](https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Documentation~/TextMeshPro/FontAssetsFallback.md)、[字体字重](https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Documentation~/TextMeshPro/FontAssetsProperties.md)、[回退材质匹配](https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Documentation~/TextMeshPro/Settings.md)、[动态字体](https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Documentation~/TextMeshPro/exportdoc.md)。
- Harmony：[参数注入](https://github.com/pardeike/Harmony/blob/master/Documentation/articles/patching-injections.md)、[Prefix](https://harmony.pardeike.net/articles/patching-prefix.html)。
- Unity：[SceneManager.sceneLoaded](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityengine/scenemanagement/scenemanager/sceneloaded)。

## 许可证

Copyright 2026 OctoberSama。源码与文档采用 [Apache License 2.0](LICENSE)，署名见 [NOTICE](NOTICE)。许可证不授予游戏或其他第三方字体资产的再分发权。
