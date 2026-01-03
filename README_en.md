<!-- markdownlint-disable MD033 MD041 -->
<div align="center"><img alt="LOGO" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/MFAAvalonia.ico" width="180" height="180" />

# MFAAvalonia

**🚀 Next-Generation Cross-Platform Automation Framework GUI**

_A universal GUI solution for [MaaFramework](https://github.com/MaaXYZ/MaaFramework) built
with [Avalonia UI](https://github.com/AvaloniaUI/Avalonia)_

[![License](https://img.shields.io/github/license/SweetSmellFox/MFAAvalonia?style=flat-square&color=4a90d9)](./LICENSE)
[![.NET](https://img.shields.io/badge/.NET-%E2%89%A5%2010-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20%7C%20Linux%20%7C%20macOS-blueviolet?style=flat-square)](https://github.com/SweetSmellFox/MFAAvalonia)
[![Commit Activity](https://img.shields.io/github/commit-activity/m/SweetSmellFox/MFAAvalonia?style=flat-square&color=00d4aa)](https://github.com/SweetSmellFox/MFAAvalonia/commits)
[![Stars](https://img.shields.io/github/stars/SweetSmellFox/MFAAvalonia?style=flat-square&color=ffca28)](https://github.com/SweetSmellFox/MFAAvalonia/stargazers)
[![Mirror Chyan](https://img.shields.io/badge/Mirror%20Chyan-%239af3f6?style=flat-square&logo=countingworkspro&logoColor=4f46e5)](https://mirrorchyan.com/zh/projects?rid=MFAAvalonia&source=mfaagh-badge)

---

**English** | [简体中文](./README.md)

</div>

## ✨ Key Features

<table>
<tr>
<td width="50%">

### 🎨 Modern Interface

- Beautiful UI powered by **SukiUI**
- **Light/Dark** theme auto-switching
- Smooth animations and interactions</td>

<td width="50%">

### 🌍 True Cross-Platform

- Full support for **Windows** / **Linux** / **macOS**
- Native performance, no extra runtime needed
- Consistent user experience across platforms

</td>
</tr>
<tr>
<td width="50%">

### ⚡ Ready Out of the Box

- Deep integration with MaaFramework project templates
- Quick deployment with simple configuration
- One-click updates via Mirror Chyan

</td>
<td width="50%">

### 🔧 Highly Customizable

- Flexible task configuration system
- Multi-language internationalization support
- Rich extension interfaces

</td>
</tr>
</table>

## 📸 Preview

<p align="center">
  <img alt="preview" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/Img/preview.png" width="100%" style="border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.15);" />
</p>

## 📋 Requirements

|   Component   | Requirement                                    |
|:-------------:|:-----------------------------------------------|
|  **Runtime**  | .NET 10.0 or higher                            |
| **Resources** | A MaaFramework-based resource project          |
|  **System**   | Windows 10+, Linux (X11/Wayland), macOS 10.15+ |

## 🚀 Quick Start

### Option 1: Automatic Installation (Recommended)

MaaFramework project templates come with MFAAvalonia pre-configured.

### Option 2: Manual Installation

<details>
<summary><b>📦 Click to expand installation steps</b></summary>

1. **Download Release**
   Download the latest version from [Releases](https://github.com/SweetSmellFox/MFAAvalonia/releases) and extract

2. **Copy Resource Files**
   ```
   maafw/assets/resource/* → MFAAvalonia/resource/
   maafw/assets/interface.json → MFAAvalonia/
   ```

3. **Configure interface.json**
   Modify the `interface.json` file according to the configuration guide below</details>

## ⚙️ Configuration Guide

### Basic Configuration Structure

```jsonc
{
  // Project Information
  "name": "Project Name",
  "version": "1.0.0",
  "url": "https://github.com/{username}/{repository}",
  "custom_title": "Custom Window Title",
  
  // Mirror Chyan Update Configuration
  "mirrorchyan_rid": "Project ID",
  "mirrorchyan_multiplatform": false,
  
  // Resource Configuration
  "resource": [
    {
      "name": "Official",
      "path": "{PROJECT_DIR}/resource/base"
    },
    {
      "name": "Bilibili",
      "path": [
        "{PROJECT_DIR}/resource/base",
        "{PROJECT_DIR}/resource/bilibili"
      ]
    }
  ],
  
  // Task Configuration
  "task": [
    {
      "name": "Task Name",
      "entry": "Task Entry",
      "default_check": true,
      "description": "Task Documentation",
      "repeatable": true,
      "repeat_count": 1
    }
  ]
}
```

### Task Configuration Details

| Field           |  Type   | Default | Description                             |
|:----------------|:-------:|:-------:|:----------------------------------------|
| `name`          | string  |    -    | Task display name                       |
| `entry`         | string  |    -    | Task entry interface                    |
| `default_check` | boolean | `false` | Whether selected by default             |
| `description`   | string  | `null`  | Task documentation (supports rich text) |
| `repeatable`    | boolean | `false` | Whether task can be repeated            |
| `repeat_count`  | number  |   `1`   | Default repeat count                    |

### 📝 Rich Text Formatting

Task documentation (`doc`) supports the following formats:

- **Markdown** - Most standard syntax supported
- **HTML** - Partial tag support
- **Custom Tags** - Extended styling support

| Tag                       | Effect            | Example                       |
|:--------------------------|:------------------|:------------------------------|
| `[color:name]...[/color]` | Text color        | `[color:red]Red text[/color]` |
| `[b]...[/b]`              | **Bold**          | `[b]Bold text[/b]`            |
| `[i]...[/i]`              | *Italic*          | `[i]Italic text[/i]`          |
| `[u]...[/u]`              | <u>Underline</u>  | `[u]Underlined text[/u]`      |
| `[s]...[/s]`              | ~~Strikethrough~~ | `[s]Strikethrough text[/s]`   |

## 🧪 Advanced Features

### Advanced Field (Experimental)

> 💡 It is recommended to use
> [InterfaceV2](https://github.com/MaaXYZ/MaaFramework/blob/main/docs/zh_cn/3.3-ProjectInterfaceV2%E5%8D%8F%E8%AE%AE.md)
> input types.

The `advanced` field allows dynamic configuration of `pipeline_override` through UI input fields, providing users with
more flexible customization options.

<details>
<summary><b>📖 View Configuration Example</b></summary>

```jsonc
{
  "task": [
    {    
      "name": "Test Task",
      "entry": "TaskA",
      "advanced": ["Advanced Setting A", "Advanced Setting B"]
    }
  ],
  "advanced": {
    "Advanced Setting A": {
      "field": "template_name",
      "type": "string",
      "default": "default.png",
      "pipeline_override": {
        "TaskA": {
          "template": "{template_name}"
        }
      }
    },
    "Advanced Setting B": {
      "field": ["x", "y"],
      "type": ["int", "int"],
      "default": ["100", "200"],
      "pipeline_override": {
        "TaskA": {
          "roi": ["{x}", "{y}", 50, 50]
        }
      }
    }
  }
}
```

**Field Descriptions:**

- `field` - Field name, supports `string` or `string[]`
- `type` - Field type, supports `string` or `string[]`
- `default` - Default value, supports `string` or `string[]`</details>

## 🛠️ Development Guide

### Multi-Language Support

Create a `lang` folder in the same directory as `interface.json` and add language files:

```
lang/
├── zh-cn.json  # Simplified Chinese
├── zh-tw.json  # Traditional Chinese
└── en-us.json  # English
```

Task names and documentation can use keys for reference, and MFAAvalonia will automatically load the corresponding
translations based on language settings.

### Announcement System

Place `.md` files in the `resource/announcement/` directory to display them as announcements. Changelog will be
automatically downloaded as an announcement when resources are updated.

### Launch Parameters

```bash
# Launch with specific configuration file
MFAAvalonia -c config-name
```

### Custom Icon

Place `logo.ico` in the program root directory to replace the window icon.

## 📄 License

This project is licensed under **[GPL-3.0 License](./LICENSE)**.

## 🙏 Acknowledgements

### Open Source Projects

| Project | Description |
|:---|:---|
| [**SukiUI**](https://github.com/kikipoulet/SukiUI) | Desktop UI Library for Avalonia |
| [**MaaFramework**](https://github.com/MaaAssistantArknights/MaaFramework) | Image Recognition Automation Framework |
| [**MaaFramework.Binding.CSharp**](https://github.com/MaaXYZ/MaaFramework.Binding.CSharp) | C# Binding for MaaFramework |
| [**Mirror Chyan**](https://github.com/MirrorChyan/docs) | Resource Update Service |
| [**Serilog**](https://github.com/serilog/serilog) | Structured Logging Library |
| [**Newtonsoft.Json**](https://github.com/JamesNK/Newtonsoft.Json) | High-performance JSON Serialization Library |
| [**AvaloniaExtensions.Axaml**](https://github.com/dotnet9/AvaloniaExtensions) | Syntax Sugar for Avalonia UI |
| [**CalcBindingAva**](https://github.com/netwww1/CalcBindingAva) | XAML Calculated Binding Extension |

### Contributors

Thanks to all developers who contributed to MFAAvalonia!

<a href="https://github.com/SweetSmellFox/MFAAvalonia/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SweetSmellFox/MFAAvalonia&max=1000" alt="Contributors"/>
</a>

<div align="center">

**If this project helps you, please give us a ⭐ Star!**

[![Star History Chart](https://api.star-history.com/svg?repos=SweetSmellFox/MFAAvalonia&type=Date)](https://star-history.com/#SweetSmellFox/MFAAvalonia&Date)

</div> 


