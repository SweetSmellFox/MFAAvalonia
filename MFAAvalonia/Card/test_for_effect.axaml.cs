// using System;
// using Avalonia;
// using Avalonia.Controls;
// using Avalonia.Interactivity;
// using Avalonia.Markup.Xaml;
// using Avalonia.Media.Imaging;
// using Avalonia.Platform.Storage;
// using MFAAvalonia.Card.ViewModel;
// using MFAAvalonia.Utilities.CardClass;
// using MFAAvalonia.ViewModels.Pages;
// using MFAAvalonia.Views.UserControls.Card;
//
// namespace MFAAvalonia.Card;
//
// public partial class test_for_effect : UserControl
// {
//     public test_for_effect()
//     {
//         InitializeComponent();
//         InitializeGlowPreview();
//     }
//
//     /// <summary>
//     /// 初始化流光预览
//     /// </summary>
//     private void InitializeGlowPreview()
//     {
//         // 尝试加载第一张玩家卡牌作为测试图片
//         Loaded += (s, e) =>
//         {
//             try
//             {
//                 var vm = DataContext as CardCollectionViewModel;
//                 if (vm?.PlayerCards.Count > 0)
//                 {
//                     GlowPreviewRenderer.Source = vm.PlayerCards[0].CardImage;
//                 }
//                 else
//                 {
//                     // 如果没有卡牌，尝试加载默认测试图片
//                     LoadDefaultTestImage();
//                 }
//             }
//             catch (Exception ex)
//             {
//                 Console.WriteLine($"[CardCollection] InitializeGlowPreview failed: {ex.Message}");
//             }
//         };
//     }
//     
//     /// <summary>
//     /// 加载默认测试图片
//     /// </summary>
//     private void LoadDefaultTestImage()
//     {
//         try
//         {
//             // 尝试从资源或默认路径加载
//             var testImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "test_card.png");
//             if (File.Exists(testImagePath))
//             {
//                 GlowPreviewRenderer.Source = new Bitmap(testImagePath);
//             }
//         }
//         catch (Exception ex)
//         {
//             Console.WriteLine($"[CardCollection] LoadDefaultTestImage failed: {ex.Message}");
//         }
//     }
//     
//     /// <summary>
//     /// 稀有度预览按钮点击事件
//     /// </summary>
//     private void OnRarityPreviewClick(object? sender, RoutedEventArgs e)
//     {
//         if (sender is Button button && button.Tag is string rarityName)
//         {
//             if (Enum.TryParse<CardRarity>(rarityName, out var rarity))
//             {
//                 // 创建一个临时的 CardViewModel 来获取对应稀有度的配置
//                 var tempBase = new CardBase
//                     { Rarity = rarity, EnableGlow = rarity != CardRarity.None && rarity != CardRarity.Normal };
//                 var tempVm = new CardViewModel(tempBase);
//
//                 if (tempVm.GlowConfig != null)
//                 {
//                     GlowPreviewRenderer.Config = tempVm.GlowConfig;
//                     GlowPreviewRenderer.IsGlowEnabled = true;
//                     // 强制刷新渲染以确保配置立即生效
//                     GlowPreviewRenderer.ForceRefresh();
//                 }
//                 else
//                 {
//                     GlowPreviewRenderer.IsGlowEnabled = false;
//                 }
//
//                 CurrentPresetText.Text = $"当前稀有度: {rarityName}";
//                 Console.WriteLine($"[CardCollection] Previewed rarity: {rarityName}");
//             }
//         }
//     }
//
//     /// <summary>
//     /// 预设按钮点击事件
//     /// </summary>
//     private void OnPresetClick(object? sender, RoutedEventArgs e)
//     {
//         if (sender is Button button && button.Tag is string presetName)
//         {
//             var preset = presetName switch
//             {
//                 "Default" => GlowPreset.Default,
//                 "GoldRare" => GlowPreset.GoldRare,
//                 "BlueRare" => GlowPreset.BlueRare,
//                 "PurpleLegend" => GlowPreset.PurpleLegend,
//                 _ => GlowPreset.Default
//             };
//
//             GlowPreviewRenderer.ApplyPreset(preset);
//             CurrentPresetText.Text = $"当前预设: {presetName}";
//
//             Console.WriteLine($"[CardCollection] Applied preset: {presetName}");
//         }
//     }
//
//
//     /// <summary>
//     /// 流光开关切换事件
//     /// </summary>
//     private void OnGlowToggleChanged(object? sender, RoutedEventArgs e)
//     {
//         if (sender is ToggleSwitch toggle)
//         {
//             GlowPreviewRenderer.IsGlowEnabled = toggle.IsChecked ?? false;
//             Console.WriteLine($"[CardCollection] Glow enabled: {GlowPreviewRenderer.IsGlowEnabled}");
//         }
//     }
//
//
//
//     /// <summary>
//     /// 选择测试图片
//     /// </summary>
//     private async void OnSelectImageClick(object? sender, RoutedEventArgs e)
//     {
//         try
//         {
//             var topLevel = TopLevel.GetTopLevel(this);
//             if (topLevel == null) return;
//
//             var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
//             {
//                 Title = "选择测试卡牌图片",
//                 AllowMultiple = false,
//                 FileTypeFilter = new[]
//                 {
//                     new FilePickerFileType("图片文件")
//                     {
//                         Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" }
//                     }
//                 }
//             });
//
//             if (files.Count > 0)
//             {
//                 var file = files[0];
//                 await using var stream = await file.OpenReadAsync();
//                 var bitmap = new Bitmap(stream);
//                 GlowPreviewRenderer.Source = bitmap;
//                 
//                 Console.WriteLine($"[CardCollection] Loaded test image: {file.Name}");
//             }
//         }
//         catch (Exception ex)
//         {
//             Console.WriteLine($"[CardCollection] OnSelectImageClick failed: {ex.Message}");
//         }
//     }
//     
// }
//
