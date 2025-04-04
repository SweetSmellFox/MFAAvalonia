<!-- markdownlint-disable MD033 MD041 -->
<p align="center">
  <img alt="LOGO" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/MFAAvalonia.ico" width="256" height="256" />
</p>

<div align="center">

# MFAAvalonia

<!-- prettier-ignore-start -->
<!-- markdownlint-disable-next-line MD036 -->
_✨ 基于 **[Avalonia](https://github.com/AvaloniaUI/Avalonia)** 的 **[MAAFramework](https://github.com/MaaXYZ/MaaFramework)** 通用 GUI 项目 ✨_
<!-- prettier-ignore-end -->

  <img alt="license" src="https://img.shields.io/github/license/SweetSmellFox/MFAAvalonia">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-≥%208-512BD4?logo=csharp">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows%20%7C%20Linux%20%7C%20macOS-blueviolet">
  <img alt="commit" src="https://img.shields.io/github/commit-activity/m/SweetSmellFox/MFAAvalonia">
</div>
<div align="center">

[English](./README_en.md) | [简体中文](./README.md)

</div>

## 预览图

<p align="center">
  <img alt="preview" src="https://github.com/SweetSmellFox/MFAAvalonia/blob/master/MFAAvalonia/Img/preview.png" height="595" width="900" />
</p>

## 使用需求

- .NET 8.0
- 一个基于`MaaFramework`的资源项目

## 说明

### 如何使用

#### 自动安装

- 下载项目中workflows/install.yml并修改```项目名称```,```作者名```,```项目名```,```MAAxxx```
- 将修改后的install.yml替换MAA项目模板.github/workflows/install.yml
- 推送新版本

#### 手动安装

- 下载最新发行版并解压
- 将maafw项目中assets/resource中所有内容复制到MFAAvalonia/Resource中
- 将maafw项目中assets/interface.json文件复制到MFAAvalonia/中
- ***修改***刚刚复制的interface.json文件
- 下面是一个例子

 ```
{
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
  "task": [
    {
      "name": "任务",
      "entry": "任务"
    }
  ]
}
```

修改为

```
{
  "name": "项目名称", //默认为null
  "version":  "项目版本", //默认为null
  "mirrorchyan_rid":  "项目ID(从Mirror酱下载的必要字段)", //默认为null , 比如 M9A
  "mirrorchyan_multiplatform":  "项目多平台字段(从Mirror酱下载的字段)", //默认为false
  "url":  "项目链接(目前应该只支持Github)", //默认为null , 比如 https://github.com/{Github账户}/{Github项目}
  "custom_title": "自定义标题", //默认为null, 使用该字段后，标题栏将只显示custom_title和version
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
  "task": [
    {
      "name": "任务",
      "entry": "任务接口",
      "check": true,  //默认为false，任务默认是否被选中
      "doc": "文档介绍",  //默认为null，显示在任务设置选项底下，可支持富文本，格式在下方
      "repeatable": true,  //默认为false，任务可不可以重复运行
      "repeat_count": 1,  //任务默认重复运行次数，需要repeatable为true
    }
  ]
}
```

可以通过controller的数量来锁定控制，可以通过controller[0]来控制默认控制器

### `doc`字符串格式：

#### 使用类似`[color:red]`文本内容`[/color]`的标记来定义文本样式。

#### 支持的标记包括：

- `[color:color_name]`：颜色，例如`[color:red]`。

-  ~~`[size:font_size]`：字号，例如`[size:20]`。~~

- `[b]`：粗体。

- `[i]`：斜体。

- `[u]`：下划线。

- `[s]`：删除线。

- `[align:left/center/right]`：居左，居中或者居右，只能在一整行中使用。

**注：上面注释内容为文档介绍用，实际运行时不建议写入。**

- 运行

## 开发相关

- 欢迎各位大佬贡献代码
- `MFA` 有interface多语言支持,在`interface.json`同目录下新建`lang`文件夹,里面内含`zh-cn.json`,`zh-tw.json`和`en-us.json`后，doc和任务的name和选项的name可以使用key来指代。MFAAvalonia会自动根据语言来读取文件的key对应的value。如果没有则默认为key
- `MFA` 会读取`Resource`文件夹的`Announcement.md`作为公告，更新资源时会自动下载一份Changelog作为公告

**注：在MFA中，于Pipeline中任务新增了多个个属性字段。**

- `focus` : *bool*  
  [原生字段] 是否启用`focus_tip` 、`focus_succeeded`、 `focus_failed`、 `focus_toast`。可选，默认false。
- `focus_toast` : *string*  
  当执行某任务前，Windows弹窗输出的内容。可选，默认空。
- `focus_tip` : *string* | *list<string, >*  
  当执行某任务前，在MFA右侧日志输出的内容。可选，默认空。
- `focus_tip_color` : *string* | *list<string, >*  
  当执行某任务前，在MFA右侧日志输出的内容的颜色。可选，默认为Gray。
- `focus_succeeded` : *string* | *list<string, >*  
  当执行某任务成功后，在MFA右侧日志输出的内容。可选，默认空。
- `focus_succeeded_color` : *string* | *list<string, >*  
  当执行某任务成功后，在MFA右侧日志输出的内容的颜色。可选，默认为Gray。
- `focus_failed` : *string* | *list<string, >*  
  当执行某任务失败时，在MFA右侧日志输出的内容。可选，默认空。
- `focus_failed_color` : *string* | *list<string, >*  
  当执行某任务失败时，在MFA右侧日志输出的内容的颜色。可选，默认为Gray。

## 许可证

**MFAAvalonia** 使用 **[GPL-3.0 许可证](./LICENSE)** 开源。

## 致谢

### 开源项目

- **[SukiUI](https://github.com/kikipoulet/SukiUI)**\
  A Desktop UI Library for Avalonia.
- **[MaaFramework](https://github.com/MaaAssistantArknights/MaaFramework)**\
  基于图像识别的自动化黑盒测试框架。
- **[Serilog](https://github.com/serilog/serilog)**\
  C# 日志记录库
- **[Newtonsoft.Json](https://github.com/CommunityToolkit/dotnet)**\
  C# JSON 库
- **[MirrorChyan](https://github.com/MirrorChyan/docs)**\
  Mirror酱更新服务
- **[AvaloniaExtensions.Axaml](https://github.com/dotnet9/AvaloniaExtensions)**\
  为Avalonia UI开发带来便利的语法糖库
- **[CalcBindingAva](https://github.com/netwww1/CalcBindingAva)**\
  CalcBinding is an advanced Binding markup extension that allows you to write calculated binding expressions in xaml, without custom converter

### 开发者

感谢所有为 **MFAAvalonia** 做出贡献的开发者。

<a href="https://github.com/SweetSmellFox/MFAAvalonia/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=SweetSmellFox/MFAAvalonia&max=1000" alt="Contributors to MFAAvalonia"/>
</a>