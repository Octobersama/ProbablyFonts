# ProbablyFonts
版本 1.0.0 · 作者 OctoberSama

为《深空当铺：可能是偷的》（Probably Stolen）补齐跨语言的 TextMesh Pro 字体回退，改善切换游戏语言后其他语言文字缺字和粗细混杂的问题。使用游戏自带字体，不包含或下载外部字体，不修改翻译或游戏语言，也没有单独的设置界面。

![游戏界面中的字体显示示例](docs/assets/font-preview.png)

截图展示英语游戏界面中正常显示的中文与母语语言名称；其中的语言选项由 ProbablyEdited 注册，ProbablyFonts 本身没有单独的设置页。

## 安装

1. 将构建产物中的整个 `dist/ProbablyFonts/` 文件夹复制到游戏 `Mods/` 目录。
2. 在游戏模组列表启用 **ProbablyFonts** 并重启游戏；之后使用游戏原有的语言选项即可。
3. 如果安装 [ProbablyEdited](https://github.com/Octobersama/ProbablyEdited)，请先安装并启用本模组，再启用 ProbablyEdited。清单依赖由原生模组加载器仅作缺失提示，不会自动拦截运行；Steam 也不会自动订阅或安装项目依赖。

其他会修改全局字体回退的模组可能互相影响。首次使用时建议停用其他全局字体模组；需要比较时一次只启用一个。

停用后重启即可恢复原有字体回退行为。本模组不保存游戏进度或个人预设。

## 构建

需要 .NET SDK 和本机安装的游戏；编译引用游戏程序集，但不会将这些程序集复制进模组包。模组目标为 `netstandard2.1`，纯逻辑检查项目使用 .NET 10。构建会生成 `dist/ProbablyFonts/`。

```powershell
dotnet build ProbablyFonts.csproj -c Release
dotnet run --project tests/ProbablyFonts.PureChecks -c Release
```

游戏安装在非默认位置时，指定 `GameDirectory`：

```powershell
dotnet build ProbablyFonts.csproj -c Release -p:GameDirectory="D:\SteamLibrary\steamapps\common\Probably Stolen"
```

GitHub Actions 会在 push、pull request 和手动运行时通过 .NET 10 的 `dotnet run` 执行 `ProbablyFonts.PureChecks`，并在成功或失败后上传名为 `ProbablyFonts-console-check-results` 的诊断产物，包含逐项 `.log` 文本日志和 `summary.txt`；它不是模组包。完整模组包仍须在安装了游戏的本机使用上述命令构建。

提交信息含 `[skip ci]`、`[ci skip]`、`[no ci]`、`[skip actions]` 或 `[actions skip]` 时，GitHub 会跳过对应的自动工作流；手动运行不受这些标记影响。

源码：[ProbablyFonts](https://github.com/Octobersama/ProbablyFonts)。

## 字体行为与边界

- 保留控件原有主字体和样式，并以游戏自带字体补充缺失字形；文字颜色、描边等外观仍由原控件决定。
- 有假名时优先使用日文字形，有谚文时使用韩文字形，注音符号使用中文字形。只有汉字时无法可靠判断中文或日文地区字形，会采用中文字形。
- 如果游戏字体无法提供所需字形，会记录警告并交回 TextMesh Pro 原有回退链；不会替换文字或伪造字形。
- 本模组通过 Harmony 适配游戏的 TextMesh Pro 字体处理。`manifest.xml` 当前声明游戏版本 `0.46D` 和 `1.0.0`；这些声明不保证所有客户端构建都兼容。游戏更新可能改变内部方法或字体结构，更新后应在目标客户端检查实际字形和界面。

## 许可证

Copyright © 2026 OctoberSama。项目代码、文档和原创封面采用 [Apache License 2.0](LICENSE)，署名见 [NOTICE](NOTICE)。演示截图中的原游戏内容及游戏字体仍属于原权利人，本项目许可不授予这些资产的再分发权。
