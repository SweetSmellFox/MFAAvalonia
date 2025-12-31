# 卡牌流光特效文档

> 基于亮度检测 + GPU Shader 实现的卡牌流光效果

## 📚 文档目录

| 文档 | 说明 | 适合人群 |
|------|------|----------|
| [01-实现原理与思路](./01-实现原理与思路.md) | 技术方案、算法原理、渲染流程 | 想了解原理的开发者 |
| [02-代码结构详解](./02-代码结构详解.md) | 类结构、方法说明、数据流 | 想修改代码的开发者 |
| [03-CardCollection接入说明](./03-CardCollection接入说明.md) | 测试面板实现、修改记录 | 想了解接入过程的开发者 |
| [04-使用说明文档](./04-使用说明文档.md) | API 参考、使用示例、UI 模板 | 想使用控件的开发者 |

## 🚀 快速开始

### 1. 基本使用

```xml
<!-- XAML -->
<card:CardGlowRenderer 
    Source="{Binding CardImage}"
    Width="200" 
    Height="300"/>
```

### 2. 应用预设

```csharp
// C#
glowRenderer.ApplyPreset(GlowPreset.GoldRare);
```

### 3. 自定义配置

```csharp
var config = new CardGlowConfig
{
    FlowColor = Color.FromRgb(255, 215, 0),
    FlowIntensity = 1.2f
};
glowRenderer.Config = config;
```

## 📁 文件结构

```
MFAAvalonia/Views/UserControls/Card/
├── CardGlowRenderer.cs      # 主控件类
├── CardGlowConfig.cs        # 配置类 + 预设
└── CardGlowShader.sksl      # Shader 源码（参考）
```

## ✨ 效果预设

| 预设 | 效果 | 适用场景 |
|------|------|----------|
| `Default` | 白色流光 | 通用 |
| `GoldRare` | 金色流光 | 金卡、稀有卡 |
| `BlueRare` | 蓝色流光 | 蓝卡、魔法卡 |
| `PurpleLegend` | 紫色流光 + 强闪烁 | 传说卡 |
| `RainbowHolo` | 高强度多彩效果 | 全息卡 |
| `Subtle` | 淡雅流光 | 低调效果 |

## 🔧 技术栈

- **Avalonia 11** - 跨平台 UI 框架
- **SkiaSharp** - 2D 图形库
- **SKSL** - Skia Shader Language
- **CompositionCustomVisual** - GPU 加速渲染

## 📋 系统要求

- .NET 6.0+
- Avalonia 11.0+
- 支持 GPU 加速的显卡（可选，有软件回退）

## 🐛 常见问题

**Q: 流光不显示？**
- 检查 `IsGlowEnabled` 是否为 true
- 检查图片是否正确加载
- 尝试调用 `ForceRefresh()`

**Q: 效果太强/太弱？**
- 调整 `OverallIntensity` 参数
- 或使用不同的预设

**Q: 性能问题？**
- 减小图片尺寸
- 对不可见的卡牌禁用流光
- 使用 `Subtle` 预设

## 📝 更新日志

### v1.0.0
- 初始版本
- 支持亮度检测流光效果
- 6 种预设配置
- CardCollection 测试面板
