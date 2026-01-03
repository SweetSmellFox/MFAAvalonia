<!-- markdownlint-disable MD033 MD041 -->
<div align="center"><img alt="LOGO" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/MFAAvalonia.ico" width="180" height="180" />

# MFAAvalonia

**🚀 新一代跨平台自动化框架图形界面**

_基于 [Avalonia UI](https://github.com/AvaloniaUI/Avalonia)
构建的 [MaaFramework](https://github.com/MaaXYZ/MaaFramework) 通用 GUI 解决方案_

[![License](https://img.shields.io/github/license/SweetSmellFox/MFAAvalonia?style=flat-square&color=4a90d9)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-%E2%89%A5%2010-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blueviolet?style=flat-square)](https://github.com/SweetSmellFox/MFAAvalonia)
[![Commit Activity](https://img.shields.io/github/commit-activity/m/SweetSmellFox/MFAAvalonia?style=flat-square&color=00d4aa)](https://github.com/SweetSmellFox/MFAAvalonia/commits)
[![Stars](https://img.shields.io/github/stars/SweetSmellFox/MFAAvalonia?style=flat-square&color=ffca28)](https://github.com/SweetSmellFox/MFAAvalonia/stargazers)
[![Mirror酱](https://img.shields.io/badge/Mirror%E9%85%B1-%239af3f6?style=flat-square&logo=countingworkspro&logoColor=4f46e5)](https://mirrorchyan.com/zh/projects?rid=MFAAvalonia&source=mfaagh-badge)

---

[English](./README_en.md) | **简体中文**

</div>

## ✨ 特性亮点

<table>
<tr>
<td width="50%">

### 🎨 现代化界面

- 基于 **SukiUI** 打造的精美界面
- 支持 **亮色/暗色** 主题自动切换
- 流畅的动画效果与交互体验</td>

<td width="50%">

### 🌍 真正的跨平台

- **Windows** / **Linux** / **macOS** 全平台支持
- 原生性能，无需额外运行时
- 统一的用户体验

</td>
</tr>
<tr>
<td width="50%">

### ⚡ 开箱即用

- 与 MaaFramework 项目模板深度集成
- 简单配置即可快速部署
- 支持 Mirror酱 一键更新

</td>
<td width="50%">

### 🔧 高度可定制

- 灵活的任务配置系统
- 支持多语言国际化
- 丰富的扩展接口

</td>
</tr>
</table>

## 📸 界面预览

<p align="center">
  <img alt="preview" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/Img/preview.png" width="100%" style="border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.15);" />
</p>

## 📋 系统要求

|   组件    | 要求                                           |
|:-------:|:---------------------------------------------|
| **运行时** | .NET 10.0 或更高版本                              |
| **资源**  | 基于 MaaFramework 的资源项目                        |
| **系统**  | Windows 10+、Linux (X11/Wayland)、macOS 10.15+ |

## 🚀 快速开始

### 方式一：自动安装（推荐）

MaaFramework 项目模板已内置 MFAAvalonia，创建项目时自动配置完成。

### 方式二：手动安装

<details>
<summary><b>📦 点击展开安装步骤</b></summary>

1. **下载发行版**

   从 [Releases](https://github.com/SweetSmellFox/MFAAvalonia/releases) 下载最新版本并解压

2. **复制资源文件**
   ```
   maafw/assets/resource/* → MFAAvalonia/resource/
   maafw/assets/interface.json → MFAAvalonia/
   ```

3. **配置 interface.json**

   根据下方配置说明修改 `interface.json` 文件</details>

## ⚙️ 配置说明

### 基础配置结构

```jsonc
{
  // 项目基本信息
  "name": "项目名称",
  "version": "1.0.0",
  "url": "https://github.com/{用户名}/{仓库名}",
  "custom_title": "自定义窗口标题",
  
  // Mirror酱更新配置
  "mirrorchyan_rid": "项目ID",
  "mirrorchyan_multiplatform": false,
  
  // 资源配置
  "resource": [
    {
      "name": "官服",
      "path": "{PROJECT_DIR}/resource/base"
    },
    {
      "name": "Bilibili服",
      "path": [
        "{PROJECT_DIR}/resource/base",
        "{PROJECT_DIR}/resource/bilibili"
      ]
    }
  ],
  
  // 任务配置
  "task": [
    {
      "name": "任务名称",
      "entry": "任务入口",
      "default_check": true,
      "description": "任务说明文档",
      "repeatable": true,
      "repeat_count": 1
    }
  ]
}
```

### 任务配置详解

| 字段              |   类型    |   默认值   | 说明            |
|:----------------|:-------:|:-------:|:--------------|
| `name`          | string  |    -    | 任务显示名称        |
| `entry`         | string  |    -    | 任务入口接口        |
| `default_check` | boolean | `false` | 是否默认选中        |
| `description`   | string  | `null`  | 任务说明文档（支持富文本） |
| `repeatable`    | boolean | `false` | 是否可重复执行       |
| `repeat_count`  | number  |   `1`   | 默认重复次数        |

### 📝 富文本格式

任务文档 (`doc`) 支持以下格式：

- **Markdown** - 支持大部分标准语法
- **HTML** - 支持部分标签
- **自定义标记** - 扩展样式支持

| 标记                      | 效果         | 示例                        |
|:------------------------|:-----------|:--------------------------|
| `[color:颜色]...[/color]` | 文字颜色       | `[color:red]红色文字[/color]` |
| `[b]...[/b]`            | **粗体**     | `[b]粗体文字[/b]`             |
| `[i]...[/i]`            | *斜体*       | `[i]斜体文字[/i]`             |
| `[u]...[/u]`            | <u>下划线</u> | `[u]下划线文字[/u]`            |
| `[s]...[/s]`            | ~~删除线~~    | `[s]删除线文字[/s]`            |

## 🧪 高级功能

### Advanced 字段（实验性）

> [!TIP]
> 推荐使用 [InterfaceV2](https://github.com/MaaXYZ/MaaFramework/blob/main/docs/zh_cn/3.3-ProjectInterfaceV2%E5%8D%8F%E8%AE%AE.md)  的 input 类型
>

`advanced` 字段允许通过 UI 输入框动态配置 `pipeline_override`，为用户提供更灵活的自定义选项。

<details>
<summary><b>📖 查看配置示例</b></summary>

```jsonc
{
  "task": [
    {    
      "name": "测试任务",
      "entry": "任务A",
      "advanced": ["高级设置A", "高级设置B"]
    }
  ],
  "advanced": {
    "高级设置A": {
      "field": "template_name",
      "type": "string",
      "default": "default.png",
      "pipeline_override": {
        "任务A": {
          "template": "{template_name}"
        }
      }
    },
    "高级设置B": {
      "field": ["x", "y"],
      "type": ["int", "int"],
      "default": ["100", "200"],
      "pipeline_override": {
        "任务A": {
          "roi": ["{x}", "{y}", 50, 50]
        }
      }
    }
  }
}
```

**字段说明：**

- `field` - 字段名称，支持 `string` 或 `string[]`
- `type` - 字段类型，支持 `string` 或 `string[]`
- `default` - 默认值，支持 `string` 或 `string[]`

</details>

## 🛠️ 开发指南

### 多语言支持

在 `interface.json` 同级目录创建 `lang` 文件夹，添加语言文件：

```
lang/
├── zh-cn.json  # 简体中文
├── zh-tw.json  # 繁体中文
└── en-us.json  # English
```

任务名称和文档可使用 key 引用，MFAAvalonia 会根据语言设置自动加载对应翻译。

### 公告系统

将 `.md` 文件放入 `resource/announcement/` 目录即可作为公告显示。资源更新时会自动下载 Changelog 作为公告。

### 启动参数

```bash
# 使用指定配置文件启动
MFAAvalonia -c 配置名称
```

### 自定义图标

将 `logo.ico` 放置在程序根目录的 `Assets` 文件夹里即可替换窗口图标。

## 📄 开源许可

本项目基于 **[GPL-3.0 License](./LICENSE)** 开源。

## 🙏 致谢

### 开源项目

| 项目 | 描述 |
|:---|:---|
| [**SukiUI**](https://github.com/kikipoulet/SukiUI) | Avalonia 桌面 UI 库 |
| [**MaaFramework**](https://github.com/MaaAssistantArknights/MaaFramework) | 图像识别自动化框架 |
| [**MaaFramework.Binding.CSharp**](https://github.com/MaaXYZ/MaaFramework.Binding.CSharp) | MaaFramework 的 C# 封装 |
| [**Mirror酱**](https://github.com/MirrorChyan/docs) | 资源更新服务 |
| [**Serilog**](https://github.com/serilog/serilog) | 结构化日志库 |
| [**Newtonsoft.Json**](https://github.com/JamesNK/Newtonsoft.Json) | 高性能 JSON 序列化库 |
| [**AvaloniaExtensions.Axaml**](https://github.com/dotnet9/AvaloniaExtensions) | Avalonia 语法糖扩展 |
| [**CalcBindingAva**](https://github.com/netwww1/CalcBindingAva) | XAML 计算绑定扩展 |

### 贡献者

感谢所有为 MFAAvalonia 做出贡献的开发者们！

<a href="https://github.com/SweetSmellFox/MFAAvalonia/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SweetSmellFox/MFAAvalonia&max=1000" alt="Contributors"/>
</a>

<div align="center">
**如果这个项目对你有帮助，请给我们一个 ⭐ Star！**

[![Star History Chart](https://api.star-history.com/svg?repos=SweetSmellFox/MFAAvalonia&type=Date)](https://star-history.com/#SweetSmellFox/MFAAvalonia&Date)

</div>



