# Avalonia For MBCC 开发记录

## 卡牌系统

### 卡牌集合页面打开慢（首屏阻塞）优化总结

**问题**
- 进入 CardCollection 页面时，首屏会卡住/白屏约 4–5 秒。
- 根因：`CardCollectionViewModel` 构造函数里同步执行 `LoadPlayerCards()`，包含本地读取/解析与对象构建，阻塞 UI 线程。

**回答**
- 将加载流程改为**后台线程执行**，并在**UI 线程回填**到 `ObservableCollection`，避免阻塞首帧渲染。
- 涉及文件：`MFAAvalonia/ViewModels/Pages/CardCollectionViewModel.cs`
- 核心改动点：
  - 构造函数里不再直接调用同步加载，而是 fire-and-forget 异步加载：
    ```csharp
    public CardCollectionViewModel()
    {
        _ = LoadPlayerCardsAsync();
        CCMgrInstance = CCMgr.Instance;
        CCMgrInstance.SetCCVM(this);
        CCMgrInstance.OnStart();
    }
    ```
  - 在后台线程读取并组装数据：`await Task.Run(() => ...)`
  - 在 UI 线程更新集合：`await Dispatcher.UIThread.InvokeAsync(() => { PlayerCards.Clear(); ... });`

### 拖拽交换卡顿（Glow 渲染/动画）优化总结

**问题**
- 卡牌拖拽交换时出现明显卡顿/掉帧。
- 根因集中在 `CardGlowRenderer` 的渲染链路：
  - glow 动画导致大量 `SKCanvas.DrawRect` 触发（CPU profile 热点）。
  - 频繁 Bitmap→SKBitmap 转换与资源生命周期处理带来额外开销。
  - 滚动列表中不可见卡片仍在持续动画与重绘。

**回答**
- 渲染侧削峰 + 资源缓存 + 视口裁剪：
  1) **共享缓存 Bitmap→SKBitmap**（进程级），避免重复转换：
     - 文件：`MFAAvalonia/Views/UserControls/Card/CardGlowRenderer.cs`
     - 使用 `ConditionalWeakTable<Bitmap, SKBitmap>` 做共享缓存。
  2) **资源所有权标记**，避免误 Dispose 共享位图：
     - 扩展 handler message（示例）：`SourceBitmapMessage(SKBitmap Bitmap, bool IsShared)` / `MaskBitmapMessage(...)`
     - Dispose 时仅在 `!IsShared` 才释放。
  3) **降帧**：将动画刷新频率限制到约 20fps，减少无效重绘。
  4) **仅视口内动画**：监听 `EffectiveViewportChanged`，滚出 `ScrollViewer` 视口即停止动画；回到视口再恢复。
  5) **减少默认负载**：`MFAAvalonia/Views/Pages/CardCollection.axaml` 中将流光测试面板默认 `IsVisible="False"`。

### 自动抽卡触发机制

在任务完成时添加了自动抽卡功能，当任务执行时间超过2分钟时自动触发抽卡奖励。

#### 修改内容
- **文件位置**: `MFAAvalonia/Extensions/MaaFW/MaaProcessor.cs`
- **修改方法**: `DisplayTaskCompletionMessage()` 
- **添加命名空间**: `using MFAAvalonia.Utilities.CardClass;`

#### 实现原理
1. **时间检测**: 在任务成功完成时，计算 `elapsedTime = DateTime.Now - _startTime`
2. **条件判断**: 当 `elapsedTime.TotalMinutes > 2` 时触发抽卡
3. **异步执行**: 使用 `TaskManager.RunTaskAsync()` 调用 `CCMgr.Instance.PullOne_real()`
4. **异常处理**: 完整的 try-catch 确保抽卡失败不影响主流程

#### 核心代码
```csharp
// 如果执行时间大于2分钟，触发抽卡
if (elapsedTime.TotalMinutes > 2)
{
    try
    {
        LoggerHelper.Info($"任务执行时间 {elapsedTime.TotalMinutes:F1} 分钟，触发自动抽卡");
        TaskManager.RunTaskAsync(async () => await CCMgr.Instance.PullOne_real(), null, "自动抽卡");
    }
    catch (Exception ex)
    {
        LoggerHelper.Error($"自动抽卡失败: {ex.Message}");
    }
}
```

#### 技术特点
- **非阻塞**: 异步执行不影响任务完成流程
- **安全性**: 异常处理确保系统稳定
- **可追踪**: 详细日志记录便于调试
- **用户体验**: 将任务成就与游戏化元素结合

### Shader控制卡牌边框颜色的原理与流程

在Avalonia中使用SkiaSharp的Shader实现动态边框效果，核心是利用GPU进行像素级别的实时渲染。

#### 整体架构

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│  CardSample     │───▶│CardBorderRenderer│───▶│  EffectDrawBase │
│  (AXAML视图)    │    │  (自定义控件)     │    │  (渲染基类)     │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                              │                        │
                              ▼                        ▼
                       ┌──────────────┐         ┌─────────────┐
                       │  SukiEffect  │────────▶│  SKShader   │
                       │ (Shader封装) │         │ (GPU执行)   │
                       └──────────────┘         └─────────────┘
```

#### 核心组件说明

| 组件 | 职责 |
|------|------|
| `CardSample.axaml` | 视图层，声明卡牌布局，嵌入`CardBorderRenderer` |
| `CardBorderRenderer` | 自定义Control，管理Shader生命周期和渲染 |
| `EffectDrawBase` | SukiUI提供的渲染基类，处理动画帧和GPU/CPU回退 |
| `SukiEffect` | Shader代码的封装，负责编译SKSL并传递Uniform变量 |
| `SKSL代码` | 实际在GPU上执行的着色器程序 |

#### 执行流程

**1. 初始化阶段**
```csharp
// CardBorderRenderer构造时，从字符串编译Shader
_sukiEffect = SukiEffect.FromString(BorderShaderCode);
```

**2. 挂载到视觉树**
```csharp
// OnAttachedToVisualTree中：
var visualHandler = new CardBorderDraw();           // 创建渲染处理器
_customVisual = comp.CreateCustomVisual(visualHandler); // 创建合成视觉
_customVisual.SendHandlerMessage(StartAnimations);  // 启动动画循环
_customVisual.SendHandlerMessage(_sukiEffect);      // 传递Shader效果
```

**3. 每帧渲染循环**
```
OnAnimationFrameUpdate() → Invalidate() → OnRender() → Render()
         ↑                                                │
         └────────────── RegisterForNextAnimationFrameUpdate ◄┘
```

**4. Shader绘制**
```csharp
protected override void Render(SKCanvas canvas, SKRect rect)
{
    using var shader = EffectWithUniforms();  // 生成带Uniform的Shader
    mainShaderPaint.Shader = shader;
    canvas.DrawRect(rect, mainShaderPaint);   // GPU执行Shader绘制
}
```

#### SKSL Shader代码解析

```glsl
vec4 main(vec2 fragCoord) {
    // 1. 坐标归一化 (0~1范围)
    vec2 uv = fragCoord / iResolution.xy;
    
    // 2. 计算像素到四边的距离
    float minDist = min(min(uv.x, 1.0-uv.x), min(uv.y, 1.0-uv.y));
    
    // 3. 判断是否在边框区域
    float inBorder = step(minDist, borderWidth);  // borderWidth内返回1，否则0
    
    // 4. 时间驱动的动态效果
    float pulse = 0.8 + 0.2 * sin(iTime * 3.0);   // 亮度脉动
    float colorShift = 0.5 + 0.5 * sin(iTime * 1.5); // 颜色渐变
    
    // 5. 混合最终颜色
    vec3 borderColor = mix(绿色, 青色, colorShift) * pulse;
    return vec4(borderColor, inBorder * iAlpha);
}
```

#### Uniform变量（CPU→GPU传递）

| 变量名 | 类型 | 说明 |
|--------|------|------|
| `iTime` | float | 动画时间（秒），驱动动态效果 |
| `iResolution` | vec3 | 控件尺寸(width, height, 0) |
| `iAlpha` | float | 整体透明度 |
| `iDark` | float | 深色主题标记(0或1) |
| `iPrimary` | vec3 | 主题主色调RGB |
| `iAccent` | vec3 | 主题强调色RGB |

#### 软件渲染回退

当GPU不可用时，自动回退到CPU绘制简单边框：
```csharp
protected override void RenderSoftware(SKCanvas canvas, SKRect rect)
{
    using var paint = new SKPaint { Color = SKColors.Green, Style = SKPaintStyle.Stroke };
    canvas.DrawRoundRect(rect, 10, 10, paint);
}
```

#### 关键要点

1. **Shader在GPU执行**：每个像素并行计算，性能极高
2. **时间驱动动画**：`iTime`变量每帧更新，Shader内用`sin(iTime)`产生周期变化
3. **Uniform传递**：CPU侧准备数据，通过`SKRuntimeEffectUniforms`传入GPU
4. **合成视觉系统**：Avalonia的`CompositionCustomVisual`实现高效渲染管线

### 基于遮罩的高性能流光技术方案

重新设计的卡牌流光方案，彻底移除了基于像素亮度的实时检测，转而使用预定义的遮罩纹理（Mask Texture）实现更加艺术化且高性能的流动效果。

#### 技术演进

| 特性 | 旧方案 (像素检测) | 新方案 (遮罩滚动) |
|------|-----------------|-----------------|
| **核心原理** | 逐像素分析亮度/饱和度/色相生成遮罩 | 对遮罩纹理进行双层 UV 滚动并混合 |
| **艺术效果** | 较生硬，受限于原图亮度分布 | 如丝绸、烟雾般灵动，效果可完全自定义 |
| **GPU 负载** | 高 (包含 HSV 转换、多层噪声计算) | 极低 (仅两次纹理采样与简单乘法) |
| **灵活性** | 固定算法，难以调整形状 | 更换 `MaskSource` 即可获得完全不同的流向 |

#### 实现原理

1. **双层遮罩混合**: 在 Shader 中将同一张遮罩图以不同的缩放 (`iFlowWidth`)、角度 (`iFlowAngle`) 和速度滚动两层。通过 `mask1 * mask2` 的方式产生非周期性的、深浅交替的动态视觉感。
2. **异步纹理加载**: 当 `MaskSource` 属性改变时，UI 线程将图像转换为 `SKBitmap` 并通过消息传递给渲染线程。渲染线程接收后立即更新 GPU 纹理 Shader，确保切换过程无卡顿。
3. **TileMode.Repeat**: 遮罩 Shader 强制开启 `Repeat` 模式，支持 UV 坐标无限滚动，适合各种循环流光效果。

#### 核心代码逻辑 (Shader)

```glsl
// 计算两层反向滚动的 UV
vec2 maskUv1 = uv * scale + dir1 * (iTime * iFlowSpeed);
vec2 maskUv2 = uv * scale * 1.2 + dir2 * (iTime * iFlowSpeed * iSecFlowSpeedMult);

// 采样遮罩纹理
float maskValue1 = iMask.eval(maskUv1 * iMaskSize).r;
float maskValue2 = iMask.eval(maskUv2 * iMaskSize).r;

// 混合并应用颜色
float combinedMask = maskValue1 * maskValue2 * 2.0;
vec3 totalGlow = (iFlowColor * maskValue1 * iFlowIntensity + iSecFlowColor * maskValue2 * iSecFlowIntensity) * combinedMask;
```

#### 优化点

- **零 GC 渲染**: 预分配了 Uniform 变量所需的 `float[]` 数组，避免在每秒 60 帧的渲染循环中产生任何内存分配。
- **即时适配**: 系统支持在运行时动态切换遮罩。例如，更换为 `mark5.jpeg` 后，流光会立即呈现出烟雾般的丝滑质感。
- **精简指令**: 移除了原有的 FBM 噪声、感知亮度计算等指令，大幅降低了移动端或低端 GPU 的发热量。

## 类说明

### CCMgr - 卡牌集合管理器

**文件位置**: `MFAAvalonia/Utilities/CardClass/CCMgr.cs`

**职责**: 卡牌系统的核心管理类，采用单例模式，负责卡牌数据管理、抽卡逻辑、UI状态同步。

#### 核心成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `Instance` | 静态属性 | 单例访问入口 |
| `CCVM` | CardCollectionViewModel | 卡牌集合视图模型引用 |
| `CardData` | List<CardBase> | 从CSV加载的全部卡牌数据 |
| `undefine` | const int (-1) | 未定义索引常量 |

#### 生命周期方法

| 方法 | 调用时机 | 功能 |
|------|---------|------|
| `OnStart()` | 应用启动 | 预留初始化入口 |
| `PostLoading()` | 加载完成后 | 执行首次抽卡 |
| `SetCCVM(vm)` | 视图初始化时 | 绑定ViewModel引用 |
| `BeforeClosed()` | 应用关闭前 | 保存玩家数据 |

#### 抽卡接口

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `PullOne()` | CardBase | 基础抽卡，返回卡牌数据并加入卡组 |
| `PullOne_real()` | Task | **完整抽卡流程**：抽卡→更新UI→弹窗展示→异常处理 |

#### Glow卡片接口

| 方法 | 参数 | 说明 |
|------|------|------|
| `AddGlowingCard()` | 无 | 添加金色传说卡（硬编码，启用发光） |
| `AddNormalCard()` | 无 | 添加普通卡（硬编码，无发光） |
| `AddCardWithGlow()` | imagePath, name, rarity, enableGlow | 自定义稀有度发光卡 |
| `AddCardWithCustomGlow()` | imagePath, name, glowConfig | 完全自定义发光配置 |
| `TestAddGlowCards()` | 无 | 测试接口，添加一金一普 |

#### UI交互接口

| 方法 | 功能 |
|------|------|
| `SetIsOpenDetail(bool)` | 控制卡牌详情面板显隐 |
| `SetSelectedCard(image, region)` | 设置选中卡片，region控制对齐方向 |
| `SwapCard(idx1, idx2)` | 交换两张卡片位置 |

#### 工具方法

| 方法 | 说明 |
|------|------|
| `LoadImageFromAssets(path)` | 从Assets加载图片，返回IImage |
| `addCard_test()` | 测试用，交替添加卡牌 |

#### 关键流程：PullOne_real()

```
1. 检查CCVM初始化状态
2. 调用PullOne()获取卡牌数据
3. 更新CCVM.PulledCard
4. 弹出PullResult窗口展示
5. 调用AddGlowingCard()添加发光卡
6. 异常时弹出ErrorView + 兜底PullResult
```

### CardCollectionViewModel - 卡牌集合视图模型

**文件位置**: `MFAAvalonia/ViewModels/Pages/CardCollectionViewModel.cs`

**职责**: 卡牌集合页面的ViewModel，基于MVVM模式，负责玩家卡牌数据绑定、UI状态管理、持久化操作。

#### 依赖引用

| 成员 | 类型 | 说明 |
|------|------|------|
| `CCMgrInstance` | CCMgr | 卡牌管理器单例引用 |
| `PlayerDataHandler` | PlayerDataHandler | 玩家数据持久化处理器 |

#### 可观察属性

| 属性 | 类型 | 说明 |
|------|------|------|
| `PlayerCards` | ObservableCollection<CardViewModel> | 玩家拥有的卡牌集合，支持UI自动刷新 |
| `IsOpenDetail` | bool | 放大面板是否打开 |
| `Hori` | HorizontalAlignment | 放大面板水平对齐方向 |
| `SelectImage` | IImage? | 放大面板中显示的卡片图片 |
| `PulledCard` | CardViewModel? | 最新抽取的卡牌 |

#### 命令

| 命令 | 触发方式 | 功能 |
|------|---------|------|
| `PullCardCommand` | UI按钮绑定 | 执行抽卡，更新PulledCard |

#### 公开方法

| 方法 | 参数 | 说明 |
|------|------|------|
| `SwapCard()` | index1, index2 | 交换两张卡片位置并更新索引 |
| `addcard()` | CardViewModel | 添加新卡片到集合末尾 |
| `SavePlayerData()` | 无 | 将PlayerCards序列化保存到本地 |

#### 构造流程

```
1. LoadPlayerCards() - 加载本地玩家数据
2. 获取CCMgr单例引用
3. SetCCVM(this) - 向CCMgr注册自身
4. OnStart() - 触发CCMgr初始化
```

#### 数据加载流程 (LoadPlayerCards)

```
1. 创建PlayerDataHandler实例
2. ReadLocal() 读取本地存储
3. 清空PlayerCards集合
4. 遍历CardBase列表，转换为CardViewModel
5. 为每个CardViewModel设置Index
```

#### 数据保存流程 (SavePlayerData)

```
1. 遍历PlayerCards集合
2. 提取Name、ImagePath、Index构建CardBase
3. 调用PlayerDataHandler.SaveLocal()持久化
```

### CardSample - 卡牌视图控件

**文件位置**: `MFAAvalonia/Views/UserControls/Card/CardSample.axaml.cs`

**职责**: 单张卡牌的UI控件，继承自UserControl，支持拖拽、发光效果、尺寸配置，通过依赖属性实现XAML绑定。

#### 依赖属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `mImage` | IImage? | null | 卡牌显示图片 |
| `IsDragbility` | bool | true | 是否可拖拽 |
| `CardWidth` | double | 200 | 卡牌宽度(像素) |
| `CardHeight` | double | 300 | 卡牌高度(像素) |
| `IsGlowEnabled` | bool | false | 是否启用发光效果 |
| `IsNormalMode` | bool | true | 是否为普通模式（与IsGlowEnabled互斥） |
| `GlowConfig` | CardGlowConfig | Default | 发光效果配置对象 |

#### 静态成员

| 成员 | 类型 | 说明 |
|------|------|------|
| `MgrIns` | CCMgr | 卡牌管理器单例引用 |

#### 属性联动机制

`IsGlowEnabled` 与 `IsNormalMode` 自动互斥同步：
```
IsGlowEnabled变化 → OnIsGlowEnabledChanged() → IsNormalMode = !IsGlowEnabled
```

#### 生命周期方法

| 方法 | 触发时机 | 功能 |
|------|---------|------|
| `构造函数` | 实例化时 | 初始化组件、获取CCMgr引用、设置默认值 |
| `OnDataContextChanged()` | DataContext变化时 | 从CardViewModel同步发光配置 |

#### DataContext同步流程

```
1. 检测DataContext是否为CardViewModel
2. 同步 IsGlowEnabled ← cardVm.EnableGlow
3. 同步 IsNormalMode ← !cardVm.EnableGlow
4. 同步 GlowConfig ← cardVm.GlowConfig ?? Default
```

#### 设计要点

- **依赖属性模式**：支持XAML双向绑定、动画、样式设置
- **属性变化回调**：通过静态构造函数注册`AddClassHandler`实现属性联动
- **ViewModel解耦**：通过DataContext自动同步，无需手动绑定

### CardGlowConfig - 卡牌流光特效配置类

**文件位置**: `MFAAvalonia/Views/UserControls/Card/CardGlowConfig.cs`

**职责**: 卡牌流光效果的参数配置类，提供丰富的可调参数和多种预设配置，用于控制Shader渲染效果。

#### 亮度检测参数

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BrightnessThreshold` | float | 0.4 | 亮度阈值(0-1)，超过此值识别为发光区域 |
| `SaturationThreshold` | float | 0.2 | 饱和度阈值(0-1) |
| `BrightnessWeight` | float | 0.6 | 亮度在发光判断中的权重 |
| `SaturationWeight` | float | 0.4 | 饱和度在发光判断中的权重 |

#### 主流光效果参数

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FlowSpeed` | float | 0.5 | 主流光移动速度 |
| `FlowWidth` | float | 0.3 | 主流光宽度(0-1) |
| `FlowAngle` | float | 0.785 | 流光角度(弧度)，默认45° |
| `FlowIntensity` | float | 0.8 | 主流光强度(0-2) |

#### 次流光效果参数

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `SecondaryFlowSpeedMultiplier` | float | 1.5 | 次流光速度倍率 |
| `SecondaryFlowWidthMultiplier` | float | 0.5 | 次流光宽度倍率 |
| `SecondaryFlowIntensity` | float | 0.4 | 次流光强度(0-1) |

#### 闪烁/流沙效果参数

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableSparkle` | bool | true | 是否启用流沙效果 |
| `SparkleFrequency` | float | 2.0 | 流沙流动频率 |
| `SparkleIntensity` | float | 0.25 | 流沙强度(0-1) |
| `SparkleDensity` | float | 0.4 | 流沙密度(0-1) |

#### 边缘辉光参数

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableEdgeGlow` | bool | true | 是否启用边缘辉光 |
| `EdgeGlowWidth` | float | 0.02 | 边缘辉光宽度(相对尺寸) |
| `EdgeGlowIntensity` | float | 0.5 | 边缘辉光强度(0-1) |

#### 颜色参数

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `FlowColor` | Color | (255,250,240) | 主流光颜色，白色偏暖 |
| `SecondaryFlowColor` | Color | (200,220,255) | 次流光颜色，淡蓝色 |
| `SparkleColor` | Color | (255,255,255) | 闪烁颜色，纯白 |
| `EdgeGlowColor` | Color | (255,230,180) | 边缘辉光颜色，淡金色 |

#### 混合模式参数

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `BlendMode` | int | 1 | 混合模式：0=Add(加法)，1=Screen(屏幕)，2=Overlay(叠加) |
| `OverallIntensity` | float | 1.0 | 整体效果强度(0-1) |

#### 色相加权参数

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableHueWeighting` | bool | true | 是否启用特殊色相加权 |
| `GoldHueWeight` | float | 0.3 | 黄色/金色色相权重 |
| `BlueHueWeight` | float | 0.2 | 蓝色/紫色色相权重 |

#### 预设配置（静态属性）

| 预设 | 适用场景 | 特点 |
|------|---------|------|
| `Default` | 默认配置 | 平衡的基础效果 |
| `GoldRare` | 金色稀有卡 | 金色流光，增强流沙，Add混合 |
| `BlueRare` | 蓝色稀有卡 | 蓝色流光，柔和效果 |
| `PurpleLegend` | 紫色传说卡 | 紫色流光，强化闪烁 |
| `RainbowHolo` | 彩虹全息卡 | 无色相加权，最强流光 |
| `Subtle` | 低调效果 | 弱化所有效果，Screen混合 |

#### 辅助方法

| 方法 | 返回值 | 说明 |
|------|--------|------|
| `ColorToFloatArray(Color)` | float[] | 将Color转为Shader用的RGB数组(0-1范围) |
| `Validate(out string)` | bool | 验证参数有效性，返回错误信息 |
| `Clone()` | CardGlowConfig | 深拷贝当前配置 |

#### 设计要点

- **参数分组**：按功能域划分region，便于查找和维护
- **预设模式**：提供多种开箱即用的稀有度配置
- **验证机制**：`Validate()`确保参数在有效范围内
- **Shader兼容**：`ColorToFloatArray()`提供GPU友好的数据格式

### CardGlowRenderer - 卡牌流光特效渲染器

**文件位置**: `MFAAvalonia/Views/UserControls/Card/CardGlowRenderer.cs`

**职责**: 基于亮度检测实现动态流光效果的自定义渲染控件，使用SkiaSharp的RuntimeEffect在GPU上执行Shader。

#### 使用方式

```xml
<!-- XAML中添加控件 -->
<card:CardGlowRenderer Source="{Binding CardImage}" />

<!-- 可选配置 -->
<card:CardGlowRenderer 
    Source="{Binding CardImage}" 
    Config="{Binding GlowConfig}"
    IsGlowEnabled="True" />
```

```csharp
// 代码中应用预设
renderer.ApplyPreset(GlowPreset.GoldRare);
```

#### 依赖属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Source` | IImage? | null | 卡牌原始图像 |
| `Config` | CardGlowConfig | Default | 流光效果配置 |
| `IsGlowEnabled` | bool | true | 是否启用流光效果 |

#### 私有字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `_customVisual` | CompositionCustomVisual? | 合成视觉对象 |
| `_visualHandler` | CardGlowDraw? | 渲染处理器实例 |
| `_sourceBitmap` | SKBitmap? | 源图像的Skia位图 |
| `_needsImageUpdate` | bool | 图像更新标记 |

#### 生命周期方法

| 方法 | 触发时机 | 功能 |
|------|---------|------|
| `OnAttachedToVisualTree()` | 挂载到视觉树 | 调用InitializeVisual初始化渲染 |
| `OnDetachedFromVisualTree()` | 从视觉树移除 | 调用CleanupResources清理资源 |
| `OnPropertyChanged()` | 属性变化时 | 分发处理Bounds/Source/Config/IsGlowEnabled变化 |

#### 初始化与清理

| 方法 | 功能 |
|------|------|
| `InitializeVisual()` | 创建CompositionCustomVisual、设置Handler、启动动画 |
| `CleanupResources()` | 停止动画、释放SKBitmap资源 |

#### 属性变化处理

| 方法 | 触发条件 | 功能 |
|------|---------|------|
| `OnSourceChanged()` | Source属性变化 | 标记需要更新图像 |
| `OnConfigChanged()` | Config属性变化 | 更新配置到渲染器 |
| `OnGlowEnabledChanged()` | IsGlowEnabled变化 | 启动/停止动画 |

#### 更新方法

| 方法 | 功能 |
|------|------|
| `UpdateVisualSize()` | 同步控件尺寸到_customVisual.Size |
| `UpdateSourceBitmap()` | 将IImage转换为SKBitmap并传递给Handler |
| `UpdateConfig()` | 验证配置并传递给Handler |
| `ConvertToSKBitmap(Bitmap)` | Avalonia Bitmap → SKBitmap (通过内存流) |
| `ConvertIImageToSKBitmap(IImage)` | 通用IImage → SKBitmap (通过RenderTargetBitmap) |

#### 公共方法

| 方法 | 参数 | 功能 |
|------|------|------|
| `ApplyPreset()` | GlowPreset | 应用预设配置(Default/GoldRare/BlueRare等) |
| `ForceRefresh()` | 无 | 强制刷新渲染(重新处理图像和配置) |

#### 内部类：CardGlowDraw

继承自 `CompositionCustomVisualHandler`，负责实际的Shader渲染。

**静态消息对象**：
| 对象 | 用途 |
|------|------|
| `StartAnimations` | 启动动画消息 |
| `StopAnimations` | 停止动画消息 |

**私有字段**：
| 字段 | 类型 | 说明 |
|------|------|------|
| `_animationTick` | Stopwatch | 动画计时器 |
| `_animationEnabled` | bool | 动画启用状态 |
| `_sourceBitmap` | SKBitmap? | 源位图引用 |
| `_imageShader` | SKShader? | 图像着色器 |
| `_effect` | SKRuntimeEffect? | 编译后的Shader效果 |
| `_config` | CardGlowConfig | 当前配置 |
| `_xxxAlloc` | float[] | 预分配的Uniform数组(避免GC) |

**核心方法**：
| 方法 | 功能 |
|------|------|
| `CompileShader()` | 编译SKSL Shader代码 |
| `SetSourceBitmap()` | 设置源位图并创建图像Shader |
| `UpdateImageShaderWithScale()` | 根据渲染尺寸更新图像Shader缩放 |
| `SetConfig()` | 设置配置并预计算颜色数组 |
| `OnMessage()` | 处理启动/停止动画消息 |
| `OnAnimationFrameUpdate()` | 每帧触发重绘 |
| `OnRender()` | 渲染入口，选择GPU或软件渲染 |
| `Render()` | GPU渲染，创建Uniform并执行Shader |
| `RenderSoftware()` | 软件回退，直接绘制原图 |

#### Shader代码结构

**Uniform变量**：
| 变量 | 类型 | 说明 |
|------|------|------|
| `iTime` | float | 动画时间(秒) |
| `iAlpha` | float | 整体透明度 |
| `iResolution` | vec3 | 渲染区域尺寸 |
| `iImage` | shader | 卡牌原始图像 |
| `iImageSize` | vec2 | 图像实际尺寸 |
| `iBrightnessThreshold` | float | 亮度阈值 |
| `iSaturationThreshold` | float | 饱和度阈值 |
| `iFlowSpeed/Width/Angle/Intensity` | float | 主流光参数 |
| `iSecFlow*` | float | 次流光参数 |
| `iSparkle*` | float | 闪烁效果参数 |
| `iEdgeGlow*` | float | 边缘辉光参数 |
| `iFlowColor/iSecFlowColor/...` | vec3 | 各效果颜色 |
| `iBlendMode` | float | 混合模式(0=Add,1=Screen,2=Overlay) |
| `iOverallIntensity` | float | 整体强度 |
| `iEnableHueWeight/iGoldHueWeight/iBlueHueWeight` | float | 色相加权参数 |

**辅助函数**：
| 函数 | 功能 |
|------|------|
| `rgb2hsv()` | RGB转HSV，用于色相分析 |
| `getLuminance()` | 计算感知亮度(ITU-R BT.601) |
| `hash()` | 高质量伪随机数生成 |
| `smoothNoise()` | 2D噪声，quintic插值 |
| `fbm3()` | 分形布朗运动噪声(3层) |
| `calculateGlowMask()` | **核心**：计算发光遮罩值 |
| `calculateFlowIntensity()` | 计算流光强度(高斯衰减光带) |
| `calculateSparkle()` | 计算流沙/微粒闪烁效果 |
| `blendColors()` | 混合模式实现(Add/Screen/Overlay) |

**主函数流程**：
```
1. 归一化坐标 uv = fragCoord / iResolution
2. 采样原始图像颜色
3. 计算发光遮罩 glowMask
4. 若遮罩<0.01直接返回原色(性能优化)
5. 计算主流光 mainFlow
6. 计算次流光 secFlow (垂直于主流光)
7. 计算流沙闪烁 sparkle
8. 合成总流光 = (主+次+闪烁) × 遮罩 × 整体强度
9. 使用混合模式合成最终颜色
```

#### GlowPreset 枚举

| 值 | 说明 |
|-----|------|
| `Default` | 默认效果 |
| `GoldRare` | 金色稀有卡 |
| `BlueRare` | 蓝色稀有卡 |
| `PurpleLegend` | 紫色传说卡 |
| `RainbowHolo` | 彩虹全息卡 |
| `Subtle` | 低调效果 |

#### 渲染流程图

```
┌─────────────────────────────────────────────────────────────┐
│                    CardGlowRenderer                         │
│  ┌─────────────┐    ┌──────────────┐    ┌───────────────┐  │
│  │   Source    │───▶│ UpdateSource │───▶│  SKBitmap     │  │
│  │  (IImage)   │    │   Bitmap()   │    │ + SKShader    │  │
│  └─────────────┘    └──────────────┘    └───────┬───────┘  │
│                                                  │          │
│  ┌─────────────┐    ┌──────────────┐            │          │
│  │   Config    │───▶│ UpdateConfig │────────────┤          │
│  │(GlowConfig) │    │     ()       │            │          │
│  └─────────────┘    └──────────────┘            ▼          │
│                                         ┌───────────────┐  │
│                                         │ CardGlowDraw  │  │
│                                         │  (Handler)    │  │
│                                         └───────┬───────┘  │
│                                                  │          │
│  ┌─────────────────────────────────────────────┐│          │
│  │              Animation Loop                 ││          │
│  │  OnAnimationFrameUpdate → Invalidate →     ◀┘          │
│  │  OnRender → Render(GPU) / RenderSoftware    │          │
│  └─────────────────────────────────────────────┘          │
└─────────────────────────────────────────────────────────────┘
```

#### 设计要点

- **合成视觉系统**：使用Avalonia的CompositionCustomVisual实现高效渲染管线
- **GPU加速**：通过SKRuntimeEffect在GPU上执行Shader，每像素并行计算
- **软件回退**：GPU不可用时自动回退到直接绘制原图
- **内存优化**：预分配Uniform数组，避免每帧GC
- **图像缩放**：通过SKMatrix将源图像缩放到渲染区域，Shader直接使用fragCoord采样
- **亮度检测算法**：结合亮度、饱和度、特殊色相加权，自动识别发光区域

### CardGlowSample - 卡牌流光特效示例控件

**文件位置**: `MFAAvalonia/Views/UserControls/Card/CardGlowSample.axaml.cs`

**职责**: 演示如何使用CardGlowRenderer的示例控件，封装了常用操作，提供预设选择和配置管理功能。

#### 依赖属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `CardImage` | IImage? | null | 卡牌显示图像 |
| `CardWidth` | double | 200 | 卡牌宽度(像素) |
| `CardHeight` | double | 300 | 卡牌高度(像素) |
| `IsGlowEnabled` | bool | true | 是否启用流光效果 |

#### 内部引用

| 成员 | 类型 | 说明 |
|------|------|------|
| `GlowRenderer` | CardGlowRenderer | XAML中定义的渲染器实例(x:Name) |

#### 事件处理

| 方法 | 触发条件 | 功能 |
|------|---------|------|
| `OnPresetChanged()` | ComboBox选择变化 | 根据SelectedIndex应用对应预设 |

**预设索引映射**：
| 索引 | 预设 |
|------|------|
| 0 | Default |
| 1 | GoldRare |
| 2 | BlueRare |
| 3 | PurpleLegend |
| 4 | RainbowHolo |
| 5 | Subtle |

#### 公开方法

| 方法 | 参数 | 返回值 | 功能 |
|------|------|--------|------|
| `LoadCardImage()` | filePath: string | void | 从文件路径加载图像到CardImage |
| `ApplyConfig()` | config: CardGlowConfig | void | 应用自定义流光配置 |
| `GetCurrentConfig()` | 无 | CardGlowConfig? | 获取当前渲染器的配置 |

#### 使用示例

```xml
<!-- XAML声明 -->
<card:CardGlowSample 
    CardImage="{Binding Image}"
    CardWidth="250"
    CardHeight="350"
    IsGlowEnabled="True" />
```

```csharp
// 代码使用
var sample = new CardGlowSample();
sample.LoadCardImage("Assets/card.png");
sample.ApplyConfig(CardGlowConfig.GoldRare);

// 获取当前配置
var config = sample.GetCurrentConfig();
```

#### 设计要点

- **封装性**：隐藏CardGlowRenderer的复杂性，提供简洁API
- **预设支持**：通过ComboBox快速切换预设效果
- **异常处理**：LoadCardImage包含try-catch，失败时输出调试信息
- **配置透传**：ApplyConfig/GetCurrentConfig直接操作内部渲染器

### CardBorderRenderer - 卡片边框Shader渲染器

**文件位置**: `MFAAvalonia/Views/UserControls/Card/CardBorderRenderer.cs`

**职责**: 使用Shader实现卡片边框的动态流光效果，继承自Control，通过CompositionCustomVisual实现GPU加速渲染。

#### 私有字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `_customVisual` | CompositionCustomVisual? | 合成视觉对象 |
| `_sukiEffect` | SukiEffect? | Shader效果封装对象 |

#### 内嵌Shader代码

**BorderShaderCode** - 黑色流光特效Shader，内嵌于类中的const字符串。

| 函数 | 功能 |
|------|------|
| `roundedRectSDF()` | 计算点到圆角矩形的有符号距离(SDF) |
| `getEdgePosition()` | 计算像素在边框上的位置参数(0~1循环) |
| `main()` | 主渲染函数，计算最终颜色 |

**Shader参数**：
| 参数 | 值 | 说明 |
|------|-----|------|
| `borderWidth` | 0.025 | 边框宽度(相对尺寸) |
| `cornerRadius` | 0.05 | 圆角半径(相对尺寸) |
| `flowSpeed` | 0.8 | 流光移动速度 |
| `flowWidth` | 0.15 | 流光宽度 |

**颜色定义**：
| 颜色 | RGB值 | 用途 |
|------|-------|------|
| `baseColor` | (0.08, 0.08, 0.1) | 基础边框色(深灰/黑) |
| `flowColor` | (0.7, 0.75, 0.85) | 主流光颜色(银白色) |
| `flowColor2` | (0.3, 0.2, 0.5) | 次流光颜色(暗紫色拖尾) |

#### 生命周期方法

| 方法 | 触发时机 | 功能 |
|------|---------|------|
| `构造函数` | 实例化时 | 从BorderShaderCode字符串创建SukiEffect |
| `OnAttachedToVisualTree()` | 挂载到视觉树 | 创建CardBorderDraw、启动动画、传递Effect |
| `OnPropertyChanged()` | 属性变化时 | 监听Bounds变化，调用Update() |

#### 公共方法

| 方法 | 参数 | 功能 |
|------|------|------|
| `SetEffect()` | SukiEffect | 替换当前Shader效果 |

#### 私有方法

| 方法 | 功能 |
|------|------|
| `Update()` | 同步控件尺寸到_customVisual.Size |

#### 内部类：CardBorderDraw

继承自 `EffectDrawBase`，负责实际的边框Shader渲染。

**构造配置**：
```csharp
AnimationEnabled = true;
AnimationSpeedScale = 1f;
```

**渲染方法**：
| 方法 | 功能 |
|------|------|
| `Render()` | GPU渲染，使用EffectWithUniforms()创建Shader绘制 |
| `RenderSoftware()` | 软件回退，绘制简单深灰色圆角边框(8px描边) |

#### 流光效果原理

```
边框位置计算 (getEdgePosition):
┌─────────────────────────┐
│     0 ~ 0.25 (上边)      │
│                         │
│0.75~1.0              0.25~0.5│
│(左边)                (右边)│
│                         │
│    0.5 ~ 0.75 (下边)     │
└─────────────────────────┘

流光效果:
- 主流光: 沿边框顺时针移动，高斯衰减
- 次流光: 落后主流光0.08相位，暗紫色拖尾
- 边缘微光: 边框外侧柔和光晕
```

#### 使用示例

```xml
<!-- XAML中使用 -->
<card:CardBorderRenderer Width="200" Height="300" />
```

```csharp
// 替换自定义Shader
var customEffect = SukiEffect.FromString(myShaderCode);
borderRenderer.SetEffect(customEffect);
```

#### 设计要点

- **SDF技术**：使用有符号距离场计算圆角矩形边框区域
- **位置参数化**：将边框位置映射到0~1循环值，便于流光动画
- **双层流光**：主流光+次流光拖尾，增加层次感
- **软件回退**：GPU不可用时绘制简单静态边框

### 基于遮罩的高性能流光技术方案

重新设计的卡牌流光方案，彻底移除了基于像素亮度的实时检测，转而使用预定义的遮罩纹理（Mask Texture）实现更加艺术化且高性能的流动效果。

#### 技术演进

| 特性 | 旧方案 (像素检测) | 新方案 (遮罩滚动) |
|------|-----------------|-----------------|
| **核心原理** | 逐像素分析亮度/饱和度/色相生成遮罩 | 对遮罩纹理进行双层 UV 滚动并混合 |
| **艺术效果** | 较生硬，受限于原图亮度分布 | 如丝绸、烟雾般灵动，效果可完全自定义 |
| **GPU 负载** | 高 (包含 HSV 转换、多层噪声计算) | 极低 (仅两次纹理采样与简单乘法) |
| **灵活性** | 固定算法，难以调整形状 | 更换 `MaskSource` 即可获得完全不同的流向 |

#### 实现原理

1. **双层遮罩混合**: 在 Shader 中将同一张遮罩图以不同的缩放 (`iFlowWidth`)、角度 (`iFlowAngle`) 和速度滚动两层。通过 `mask1 * mask2` 的方式产生非周期性的、深浅交替的动态视觉感。
2. **异步纹理加载**: 当 `MaskSource` 属性改变时，UI 线程将图像转换为 `SKBitmap` 并通过消息传递给渲染线程。渲染线程接收后立即更新 GPU 纹理 Shader，确保切换过程无卡顿。
3. **TileMode.Repeat**: 遮罩 Shader 强制开启 `Repeat` 模式，支持 UV 坐标无限滚动，适合各种循环流光效果。

#### 核心代码逻辑 (Shader)

```glsl
// 计算两层反向滚动的 UV
vec2 maskUv1 = uv * scale + dir1 * (iTime * iFlowSpeed);
vec2 maskUv2 = uv * scale * 1.2 + dir2 * (iTime * iFlowSpeed * iSecFlowSpeedMult);

// 采样遮罩纹理
float maskValue1 = iMask.eval(maskUv1 * iMaskSize).r;
float maskValue2 = iMask.eval(maskUv2 * iMaskSize).r;

// 混合并应用颜色
float combinedMask = maskValue1 * maskValue2 * 2.0;
vec3 totalGlow = (iFlowColor * maskValue1 * iFlowIntensity + iSecFlowColor * maskValue2 * iSecFlowIntensity) * combinedMask;
```

#### 优化点

- **零 GC 渲染**: 预分配了 Uniform 变量所需的 `float[]` 数组，避免在每秒 60 帧的渲染循环中产生任何内存分配。
- **即时适配**: 系统支持在运行时动态切换遮罩。例如，更换为 `mark5.jpeg` 后，流光会立即呈现出烟雾般的丝滑质感。
- **精简指令**: 移除了原有的 FBM 噪声、感知亮度计算等指令，大幅降低了移动端或低端 GPU 的发热量。