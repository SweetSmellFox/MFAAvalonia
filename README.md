<!-- markdownlint-disable MD033 MD041 -->
<div align="center"><img alt="LOGO" src="./docs/images/mfa-logo_512x512.png" width="256" height="256" />

# MFAAvalonia 

**🚀 新一代跨平台自动化框架图形界面**

_基于 [Avalonia UI](https://github.com/AvaloniaUI/Avalonia)
构建的 [MaaFramework](https://github.com/MaaXYZ/MaaFramework) 通用 GUI 解决方案_

[![License](https://img.shields.io/github/license/MaaXYZ/MFAAvalonia?style=flat-square&color=4a90d9)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-%E2%89%A5%2010-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blueviolet?style=flat-square)](https://github.com/MaaXYZ/MFAAvalonia)
[![Commit Activity](https://img.shields.io/github/commit-activity/m/MaaXYZ/MFAAvalonia?style=flat-square&color=00d4aa)](https://github.com/MaaXYZ/MFAAvalonia/commits)
[![Stars](https://img.shields.io/github/stars/MaaXYZ/MFAAvalonia?style=flat-square&color=ffca28)](https://github.com/MaaXYZ/MFAAvalonia/stargazers)
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
  <img alt="preview" src="./docs/images/preview.png" width="100%" style="border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.15);" />
</p>

## 📋 系统要求

|   组件    | 要求                                           |
|:-------:|:---------------------------------------------|
| **运行时** | .NET 10.0 或更高版本                              |
| **资源**  | 基于 MaaFramework 的资源项目                        |
| **系统**  | Windows 10+、Linux (X11/Wayland)、macOS 10.15+ |

## 🚀 快速开始

本项目是 [MaaFramework](https://github.com/MaaXYZ/MaaFramework) 通用 GUI 解决方案，使用前请务必确保您对 [MaaFramework](https://github.com/MaaXYZ/MaaFramework) 有基本的了解，并且至少已经**完成**了_一个阶段_的[开发工作](https://github.com/MaaXYZ/MaaPracticeBoilerplate/blob/main/docs/zh_cn/develop/how_to_develop.md)。**不要在开发阶段使用本项目进行调试，你应该使用专门的[调试工具](https://github.com/MaaXYZ/MaaFramework/#%E5%BC%80%E5%8F%91%E5%B7%A5%E5%85%B7)**

### 方式一：自动安装（推荐）

MaaFramework [项目模板](https://github.com/MaaXYZ/MaaPracticeBoilerplate/)已内置 MFAAvalonia 的自动配置工具。

有关自动安装的方法请**仔细阅读**[项目模板]的[如何开发](https://github.com/MaaXYZ/MaaPracticeBoilerplate/blob/main/docs/zh_cn/develop/how_to_develop.md)文档。

### 方式二：手动安装
 
<details>
<summary><b>📦 点击展开安装步骤</b></summary>

**绝大多数情况下，你不应该手动安装。**

1. **下载发行版**

   从 [Releases](https://github.com/MaaXYZ/MFAAvalonia/releases) 下载最新版本并解压

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

### 控制器配置详解

`controller` 为对象数组，用于声明控制器预设与类型配置：

| 字段 | 类型 | 说明 |
|:--|:--|:--|
| `name` | string | 唯一名称标识符，用作控制器 ID |
| `label` | string | 显示名称，支持国际化（以 `$` 开头）。未设置时显示 `name` |
| `description` | string | 详细描述，支持文件路径、URL 或直接文本，内容支持 Markdown，支持国际化 |
| `icon` | string | 图标路径（相对项目根目录），支持国际化 |
| `type` | `'Adb' \| 'Win32' \| 'PlayCover'` | 控制器类型 |
| `display_short_side` | number | 默认缩放分辨率短边长度，默认 720。与 `display_long_side` / `display_raw` 互斥 |
| `display_long_side` | number | 默认缩放分辨率长边长度。与 `display_short_side` / `display_raw` 互斥 |
| `display_raw` | boolean | 是否使用原始分辨率截图。与缩放分辨率设置互斥 |
| `adb` | object | Adb 控制器配置（V2 中 input/screencap 由框架自动检测） |
| `win32` | object | Win32 控制器配置 |
| `playcover` | object | PlayCover 控制器配置（仅 macOS） |

`win32` 字段：

| 字段 | 类型 | 说明 |
|:--|:--|:--|
| `class_regex` | string | 可选。窗口类名正则 |
| `window_regex` | string | 可选。窗口标题正则 |
| `mouse` | string | 可选。鼠标控制方式 |
| `keyboard` | string | 可选。键盘控制方式 |
| `screencap` | string | 可选。截图方式 |

`playcover` 字段：

| 字段 | 类型 | 说明 |
|:--|:--|:--|
| `uuid` | string | 可选。目标应用 Bundle Identifier，默认 `maa.playcover` |

### 任务配置详解

#### 外部通知
- [外部通知填写指南](./docs/zh/外部通知.md)

#### 自定义布局
- [自定义布局说明](./docs/zh/自定义布局.md)

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

### 🎯 Focus 协议

`focus` 用于在任务执行过程中输出关键提示、Toast 或日志。支持 **旧协议** 与 **新协议**，写在任务节点中：

- **旧协议**：字段 `start / succeeded / failed / toast / aborted`
- **新协议**：以 **消息类型** 为键，值为字符串或字符串数组

消息类型使用 MaaFramework 的节点事件常量，例如：
- 识别阶段：`Node.Recognition.Starting` / `Node.Recognition.Succeeded` / `Node.Recognition.Failed`
- 动作阶段：`Node.Action.Starting` / `Node.Action.Succeeded` / `Node.Action.Failed`

新协议会按消息类型匹配并渲染到日志。

**旧协议示例：**
```jsonc
{
  "focus": {
    "start": ["[color:cyan]开始执行[/color]"],
    "succeeded": ["[color:green]任务完成[/color]"],
    "failed": ["[color:red]任务失败[/color]"],
    "toast": ["提示标题", "提示内容"],
    "aborted": true
  }
}
```

**旧协议字段说明：**
- `toast`：数组长度 >= 1 时弹出 Toast；第 1 项为标题，第 2 项为内容（可省略）
- `aborted`：为 `true` 时在 `Starting` 阶段触发中止回调（用于中断任务）

**新协议示例：**
```jsonc
{
  "focus": {
    "Node.Action.Starting": "开始：{name}",
    "Node.Action.Succeeded": "完成: {name}",
    "Node.Action.Failed": "失败ID：{action_id}"
  }
}
```

**占位符与变量：**
- `{key}` 会从 `details` 中替换对应字段
- 旧协议中的日志/Toast 支持计数变量，如 `{count}`、`{++count}`、`{count++}`、`{count+1}`

## 🧪 高级功能

### Advanced 字段（废弃）

> [!TIP]
> `Advanced` 字段已基本被[InterfaceV2](https://github.com/MaaXYZ/MaaFramework/blob/main/docs/zh_cn/3.3-ProjectInterfaceV2%E5%8D%8F%E8%AE%AE.md) 的 input 类型替代，不建议使用
>

## 🛠️ 开发指南

### 多语言支持

在 `interface.json` 同级目录创建 `lang` 文件夹，添加语言文件：

```
lang/
├── zh-cn.json  # 简体中文
├── zh-tw.json  # 繁体中文
└── en-us.json  # English
```

同时需要在 `interface.json` 中新增多语言字段（路径相对于 `interface.json`）：

```jsonc
{
  "languages": {
    "zh-cn": "lang/zh-cn.json",
    "zh-tw": "lang/zh-tw.json",
    "en-us": "lang/en-us.json"
  }
}
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

<a href="https://github.com/MaaXYZ/MFAAvalonia/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=MaaXYZ/MFAAvalonia&max=1000" alt="Contributors"/>
</a>

<div align="center">
**如果这个项目对你有帮助，请给我们一个 ⭐ Star！**

[![Star History Chart](https://api.star-history.com/svg?repos=MaaXYZ/MFAAvalonia&type=Date)](https://star-history.com/#MaaXYZ/MFAAvalonia&Date)

</div>
