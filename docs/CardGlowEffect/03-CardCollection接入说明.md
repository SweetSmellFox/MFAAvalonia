# CardCollection 页面接入流光效果说明

> 本文档记录如何将 `CardGlowRenderer` 控件接入到 `CardCollection` 页面进行测试。

## 一、修改概览

| 文件 | 修改内容 |
|------|----------|
| `CardCollection.axaml` | 添加测试面板 UI |
| `CardCollection.axaml.cs` | 添加事件处理方法 |

## 二、AXAML 修改详解

### 2.1 布局结构调整

**修改前**：单一 Grid 布局
```xml
<Grid>
    <!-- 主内容 -->
</Grid>
```

**修改后**：两行 Grid 布局
```xml
<Grid>
    <Grid.RowDefinitions>
        <RowDefinition Height="*"/>      <!-- 主内容区域 -->
        <RowDefinition Height="Auto"/>   <!-- 测试面板 -->
    </Grid.RowDefinitions>
    
    <!-- Row 0: 主内容区域 -->
    <Grid Grid.Row="0">
        <!-- 原有的卡牌列表、细节栏等 -->
    </Grid>
    
    <!-- Row 1: 流光效果测试面板 -->
    <Border Grid.Row="1">
        <!-- 测试面板内容 -->
    </Border>
</Grid>
```

### 2.2 测试面板结构

```xml
<!-- 流光效果测试面板 -->
<Border Grid.Row="1" 
        Background="#2D2D30" 
        BorderBrush="#3F3F46" 
        BorderThickness="0,1,0,0"
        Padding="10">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>   <!-- 预览区 -->
            <ColumnDefinition Width="*"/>      <!-- 控制面板 -->
        </Grid.ColumnDefinitions>
        
        <!-- 左侧：流光卡牌预览 -->
        <Border Grid.Column="0">
            <StackPanel>
                <TextBlock Text="流光效果预览"/>
                <local:CardGlowRenderer 
                    x:Name="GlowPreviewRenderer"
                    Width="200" 
                    Height="300"
                    IsGlowEnabled="True"/>
            </StackPanel>
        </Border>
        
        <!-- 右侧：控制面板 -->
        <Border Grid.Column="1">
            <!-- 预设按钮、开关、选择图片按钮 -->
        </Border>
    </Grid>
</Border>
```

### 2.3 预设按钮

```xml
<WrapPanel Orientation="Horizontal">
    <Button Content="默认" 
            Click="OnPresetClick" 
            Tag="Default"
            Margin="5" Padding="15,8"/>
    
    <Button Content="金色稀有" 
            Click="OnPresetClick" 
            Tag="GoldRare"
            Background="#FFD700"/>
    
    <Button Content="蓝色稀有" 
            Click="OnPresetClick" 
            Tag="BlueRare"
            Background="#4169E1"/>
    
    <Button Content="紫色传说" 
            Click="OnPresetClick" 
            Tag="PurpleLegend"
            Background="#9932CC"/>
    
    <Button Content="彩虹全息" 
            Click="OnPresetClick" 
            Tag="RainbowHolo"
            Background="#FF69B4"/>
    
    <Button Content="低调效果" 
            Click="OnPresetClick" 
            Tag="Subtle"
            Background="#555555"/>
</WrapPanel>
```

**关键点**：
- 使用 `Tag` 属性存储预设名称
- 所有按钮共用同一个 `Click` 事件处理器

### 2.4 开关控制

```xml
<StackPanel Orientation="Horizontal" Margin="0,15,0,0">
    <TextBlock Text="启用流光效果:" 
               Foreground="White" 
               VerticalAlignment="Center"/>
    <ToggleSwitch x:Name="GlowToggle" 
                  IsChecked="True"
                  IsCheckedChanged="OnGlowToggleChanged"/>
</StackPanel>
```

### 2.5 选择图片按钮

```xml
<Button Content="选择测试图片" 
        Click="OnSelectImageClick"
        Margin="5,15,5,0"
        Padding="15,8"/>
```

### 2.6 当前预设显示

```xml
<TextBlock x:Name="CurrentPresetText"
           Text="当前预设: Default" 
           Foreground="#888888" 
           FontSize="12"
           Margin="5,10,0,0"/>
```

## 三、代码后端修改详解

### 3.1 新增 using 引用

```csharp
using System.IO;                    // 文件操作
using Avalonia.Media.Imaging;       // Bitmap
using Avalonia.Platform.Storage;    // 文件选择器
```

### 3.2 初始化流光预览

```csharp
public CardCollection()
{
    InitializeComponent();
    // ... 原有代码 ...
    
    // 新增：初始化流光预览
    InitializeGlowPreview();
}

private void InitializeGlowPreview()
{
    // 在 Loaded 事件中加载图片，确保控件已完全初始化
    Loaded += (s, e) =>
    {
        try
        {
            var vm = DataContext as CardCollectionViewModel;
            if (vm?.PlayerCards.Count > 0)
            {
                // 使用第一张玩家卡牌作为测试图片
                GlowPreviewRenderer.Source = vm.PlayerCards[0].CardImage;
            }
            else
            {
                // 没有卡牌时加载默认测试图片
                LoadDefaultTestImage();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"InitializeGlowPreview failed: {ex.Message}");
        }
    };
}
```

### 3.3 加载默认测试图片

```csharp
private void LoadDefaultTestImage()
{
    try
    {
        var testImagePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "Assets", 
            "test_card.png");
            
        if (File.Exists(testImagePath))
        {
            GlowPreviewRenderer.Source = new Bitmap(testImagePath);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"LoadDefaultTestImage failed: {ex.Message}");
    }
}
```

### 3.4 预设按钮点击事件

```csharp
private void OnPresetClick(object? sender, RoutedEventArgs e)
{
    // 1. 获取按钮的 Tag（预设名称）
    if (sender is Button button && button.Tag is string presetName)
    {
        // 2. 将字符串转换为枚举
        var preset = presetName switch
        {
            "Default" => GlowPreset.Default,
            "GoldRare" => GlowPreset.GoldRare,
            "BlueRare" => GlowPreset.BlueRare,
            "PurpleLegend" => GlowPreset.PurpleLegend,
            "RainbowHolo" => GlowPreset.RainbowHolo,
            "Subtle" => GlowPreset.Subtle,
            _ => GlowPreset.Default
        };
        
        // 3. 应用预设
        GlowPreviewRenderer.ApplyPreset(preset);
        
        // 4. 更新显示文本
        CurrentPresetText.Text = $"当前预设: {presetName}";
        
        Console.WriteLine($"Applied preset: {presetName}");
    }
}
```

### 3.5 流光开关切换事件

```csharp
private void OnGlowToggleChanged(object? sender, RoutedEventArgs e)
{
    if (sender is ToggleSwitch toggle)
    {
        // 直接设置 IsGlowEnabled 属性
        GlowPreviewRenderer.IsGlowEnabled = toggle.IsChecked ?? false;
        
        Console.WriteLine($"Glow enabled: {GlowPreviewRenderer.IsGlowEnabled}");
    }
}
```

### 3.6 选择测试图片

```csharp
private async void OnSelectImageClick(object? sender, RoutedEventArgs e)
{
    try
    {
        // 1. 获取 TopLevel（窗口）
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        
        // 2. 打开文件选择器
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "选择测试卡牌图片",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" }
                    }
                }
            });
        
        // 3. 加载选中的图片
        if (files.Count > 0)
        {
            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            var bitmap = new Bitmap(stream);
            GlowPreviewRenderer.Source = bitmap;
            
            Console.WriteLine($"Loaded test image: {file.Name}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"OnSelectImageClick failed: {ex.Message}");
    }
}
```

## 四、完整代码区域

### 4.1 AXAML 新增部分

```xml
<!-- 流光效果测试面板 -->
<Border Grid.Row="1" 
        Background="#2D2D30" 
        BorderBrush="#3F3F46" 
        BorderThickness="0,1,0,0"
        Padding="10">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="*"/>
        </Grid.ColumnDefinitions>
        
        <!-- 流光卡牌预览 -->
        <Border Grid.Column="0" 
                Background="#1E1E1E" 
                CornerRadius="8"
                Padding="10"
                Margin="0,0,10,0">
            <StackPanel>
                <TextBlock Text="流光效果预览" 
                           Foreground="White" 
                           FontWeight="Bold"
                           Margin="0,0,0,10"
                           HorizontalAlignment="Center"/>
                <!-- 流光渲染器 -->
                <local:CardGlowRenderer 
                    x:Name="GlowPreviewRenderer"
                    Width="200" 
                    Height="300"
                    IsGlowEnabled="True"/>
            </StackPanel>
        </Border>
        
        <!-- 控制面板 -->
        <Border Grid.Column="1" 
                Background="#1E1E1E" 
                CornerRadius="8"
                Padding="15">
            <StackPanel>
                <TextBlock Text="流光预设选择" 
                           Foreground="White" 
                           FontWeight="Bold"
                           FontSize="14"
                           Margin="0,0,0,10"/>
                
                <!-- 预设按钮 -->
                <WrapPanel Orientation="Horizontal">
                    <Button Content="默认" Click="OnPresetClick" Tag="Default"
                            Margin="5" Padding="15,8"/>
                    <Button Content="金色稀有" Click="OnPresetClick" Tag="GoldRare"
                            Margin="5" Padding="15,8" Background="#FFD700"/>
                    <Button Content="蓝色稀有" Click="OnPresetClick" Tag="BlueRare"
                            Margin="5" Padding="15,8" Background="#4169E1"/>
                    <Button Content="紫色传说" Click="OnPresetClick" Tag="PurpleLegend"
                            Margin="5" Padding="15,8" Background="#9932CC"/>
                    <Button Content="彩虹全息" Click="OnPresetClick" Tag="RainbowHolo"
                            Margin="5" Padding="15,8" Background="#FF69B4"/>
                    <Button Content="低调效果" Click="OnPresetClick" Tag="Subtle"
                            Margin="5" Padding="15,8" Background="#555555"/>
                </WrapPanel>
                
                <!-- 开关控制 -->
                <StackPanel Orientation="Horizontal" Margin="0,15,0,0">
                    <TextBlock Text="启用流光效果:" Foreground="White" 
                               VerticalAlignment="Center" Margin="5,0,10,0"/>
                    <ToggleSwitch x:Name="GlowToggle" IsChecked="True"
                                  IsCheckedChanged="OnGlowToggleChanged"/>
                </StackPanel>
                
                <!-- 选择图片按钮 -->
                <Button Content="选择测试图片" Click="OnSelectImageClick"
                        Margin="5,15,5,0" Padding="15,8"/>
                
                <!-- 当前预设显示 -->
                <TextBlock x:Name="CurrentPresetText" Text="当前预设: Default" 
                           Foreground="#888888" FontSize="12" Margin="5,10,0,0"/>
            </StackPanel>
        </Border>
    </Grid>
</Border>
```

### 4.2 C# 新增部分

```csharp
#region 流光效果测试

private void InitializeGlowPreview()
{
    Loaded += (s, e) =>
    {
        try
        {
            var vm = DataContext as CardCollectionViewModel;
            if (vm?.PlayerCards.Count > 0)
            {
                GlowPreviewRenderer.Source = vm.PlayerCards[0].CardImage;
            }
            else
            {
                LoadDefaultTestImage();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CardCollection] InitializeGlowPreview failed: {ex.Message}");
        }
    };
}

private void LoadDefaultTestImage()
{
    try
    {
        var testImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "test_card.png");
        if (File.Exists(testImagePath))
        {
            GlowPreviewRenderer.Source = new Bitmap(testImagePath);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CardCollection] LoadDefaultTestImage failed: {ex.Message}");
    }
}

private void OnPresetClick(object? sender, RoutedEventArgs e)
{
    if (sender is Button button && button.Tag is string presetName)
    {
        var preset = presetName switch
        {
            "Default" => GlowPreset.Default,
            "GoldRare" => GlowPreset.GoldRare,
            "BlueRare" => GlowPreset.BlueRare,
            "PurpleLegend" => GlowPreset.PurpleLegend,
            "RainbowHolo" => GlowPreset.RainbowHolo,
            "Subtle" => GlowPreset.Subtle,
            _ => GlowPreset.Default
        };

        GlowPreviewRenderer.ApplyPreset(preset);
        CurrentPresetText.Text = $"当前预设: {presetName}";
        Console.WriteLine($"[CardCollection] Applied preset: {presetName}");
    }
}

private void OnGlowToggleChanged(object? sender, RoutedEventArgs e)
{
    if (sender is ToggleSwitch toggle)
    {
        GlowPreviewRenderer.IsGlowEnabled = toggle.IsChecked ?? false;
        Console.WriteLine($"[CardCollection] Glow enabled: {GlowPreviewRenderer.IsGlowEnabled}");
    }
}

private async void OnSelectImageClick(object? sender, RoutedEventArgs e)
{
    try
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择测试卡牌图片",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片文件")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" }
                }
            }
        });

        if (files.Count > 0)
        {
            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            var bitmap = new Bitmap(stream);
            GlowPreviewRenderer.Source = bitmap;
            Console.WriteLine($"[CardCollection] Loaded test image: {file.Name}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CardCollection] OnSelectImageClick failed: {ex.Message}");
    }
}

#endregion
```

## 五、运行效果

### 5.1 界面布局

```
┌─────────────────────────────────────────────────────────────┐
│                                                             │
│                      卡牌列表区域                            │
│                                                             │
│    ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐                │
│    │卡牌1│ │卡牌2│ │卡牌3│ │卡牌4│ │卡牌5│                │
│    └─────┘ └─────┘ └─────┘ └─────┘ └─────┘                │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│  ┌───────────┐  ┌────────────────────────────────────────┐ │
│  │           │  │ 流光预设选择                            │ │
│  │  流光效果  │  │ [默认] [金色稀有] [蓝色稀有] [紫色传说] │ │
│  │   预览    │  │ [彩虹全息] [低调效果]                   │ │
│  │           │  │                                        │ │
│  │  ┌─────┐  │  │ 启用流光效果: [✓]                      │ │
│  │  │     │  │  │                                        │ │
│  │  │ 卡牌 │  │  │ [选择测试图片]                         │ │
│  │  │     │  │  │                                        │ │
│  │  └─────┘  │  │ 当前预设: Default                      │ │
│  └───────────┘  └────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

### 5.2 使用步骤

1. **运行程序**，进入 CardCollection 页面
2. 页面底部会显示**流光效果测试面板**
3. 默认会加载第一张玩家卡牌作为测试图片
4. 点击不同的**预设按钮**查看不同的流光效果
5. 使用**开关**控制流光效果的开启/关闭
6. 点击**"选择测试图片"**按钮可以选择其他图片进行测试

## 六、注意事项

1. **GPU 要求**：流光效果需要 GPU 支持，如果 GPU 不可用会回退到显示原图
2. **图片格式**：支持 PNG、JPG、JPEG、BMP、GIF 格式
3. **性能**：大尺寸图片可能影响性能，建议使用适当尺寸的卡牌图片
4. **预设调整**：如果效果不理想，可以修改 `CardGlowConfig` 中的预设参数
