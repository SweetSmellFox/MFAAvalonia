﻿namespace MFAAvalonia.Configuration;

public static class ConfigurationKeys
{
    #region 全局设置

    public const string DefaultConfig = "DefaultConfig";
    public const string ShowGui = "ShowGui";
    public const string LinkStart = "LinkStart";
    public const string DoNotShowAnnouncementAgain = "AnnouncementInfo.DoNotShowAgain";
    public const string DoNotShowChangelogAgain = "Changelog.DoNotShowAgain";
    public const string ForceScheduledStart = "ForceScheduledStart";
    public const string CustomConfig = "CustomConfig";
    public const string NoAutoStart = "NoAutoStart";

    #endregion

    #region 主页设置

    public const string EnableEdit = "EnableEdit";
    public const string TaskItems = "TaskItems";

    #endregion

    #region 启动设置

    public const string BeforeTask = "BeforeTask";
    public const string AfterTask = "AfterTask";
    public const string AutoMinimize = "AutoMinimize";
    public const string AutoHide = "AutoHide";
    public const string SoftwarePath = "SoftwarePath";
    public const string WaitSoftwareTime = "WaitSoftwareTime";
    public const string EmulatorConfig = "EmulatorConfig";

    #endregion

    #region 性能设置

    public const string GPUOption = "GPUOption";

    #endregion

    #region 连接设置

    public const string RememberAdb = "RememberAdb";
    public const string AdbControlScreenCapType = "AdbControlScreenCapType";
    public const string AdbControlInputType = "AdbControlInputType";
    public const string Win32ControlScreenCapType = "Win32ControlScreenCapType";
    public const string Win32ControlInputType = "Win32ControlInputType";
    public const string AllowAdbRestart = "AllowAdbRestart";
    public const string AllowAdbHardRestart = "AllowAdbHardRestart";
    public const string RetryOnDisconnected = "RetryOnDisconnected";
    public const string AdbDevice = "AdbDevice";
    public const string CurrentController = "CurrentController";

    #endregion

    #region 游戏设置

    public const string Resource = "Resource";
    public const string Recording = "recording";
    public const string SaveDraw = "save_draw";
    public const string ShowHitDraw = "show_hit_box";
    public const string Prescript = "Prescript";
    public const string Postscript = "Post-script";
    public const string ContinueRunningWhenError = "ContinueRunningWhenError";
    
    #endregion

    #region 界面设置

    public const string LangIndex = "LangIndex";
    public const string CurrentLanguage = "CurrentLanguage";
    public const string ThemeIndex = "ThemeIndex";
    public const string ShouldMinimizeToTray = "ShouldMinimizeToTray";
    public const string EnableShowIcon = "EnableShowIcon";

    public const string OtherColorTheme = "OtherColorTheme";
    public const string BackgroundStyle = "BackgroundStyle";
    public const string BaseTheme = "BaseTheme";
    public const string BackgroundAnimations = "BackgroundAnimations";
    public const string BackgroundTransitions = "BackgroundTransitions";
    public const string ColorTheme = "ColorTheme";

    #endregion

    #region 外部通知

    public const string ExternalNotificationEnabled = "ExternalNotificationEnabled";
    public const string ExternalNotificationDingTalkToken = "ExternalNotificationDingTalkToken";
    public const string ExternalNotificationDingTalkSecret = "ExternalNotificationDingTalkSecret";
    public const string ExternalNotificationEmailAccount = "ExternalNotificationEmailAccount";
    public const string ExternalNotificationEmailSecret = "ExternalNotificationEmailSecret";
    public const string ExternalNotificationLarkID = "ExternalNotificationLarkID";
    public const string ExternalNotificationLarkToken = "ExternalNotificationLarkToken";
    public const string ExternalNotificationWxPusherToken = "ExternalNotificationWxPusherToken";
    public const string ExternalNotificationWxPusherUID = "ExternalNotificationWxPusherUID";
    public const string ExternalNotificationTelegramBotToken = "ExternalNotificationTelegramBotToken";
    public const string ExternalNotificationTelegramChatId = "ExternalNotificationTelegramChatId";
    public const string ExternalNotificationDiscordBotToken = "ExternalNotificationDiscordBotToken";
    public const string ExternalNotificationDiscordChannelId = "ExternalNotificationDiscordChannelId";
    public const string ExternalNotificationDiscordWebhookUrl = "ExternalNotificationDiscordWebhookUrl";
    public const string ExternalNotificationDiscordWebhookName = "ExternalNotificationDiscordWebhookName";
    public const string ExternalNotificationSmtpServer = "ExternalNotificationSmtpServer";
    public const string ExternalNotificationSmtpPort = "ExternalNotificationSmtpPort";
    public const string ExternalNotificationSmtpUser = "ExternalNotificationSmtpUser";
    public const string ExternalNotificationSmtpPassword = "ExternalNotificationSmtpPassword";
    public const string ExternalNotificationSmtpFrom = "ExternalNotificationSmtpFrom";
    public const string ExternalNotificationSmtpTo = "ExternalNotificationSmtpTo";
    public const string ExternalNotificationSmtpUseSsl = "ExternalNotificationSmtpUseSsl";
    public const string ExternalNotificationSmtpRequiresAuthentication = "ExternalNotificationSmtpRequiresAuthentication";
    public const string ExternalNotificationQmsgServer = "ExternalNotificationQmsgServer";
    public const string ExternalNotificationQmsgKey = "ExternalNotificationQmsgKey";
    public const string ExternalNotificationQmsgUser = "ExternalNotificationQmsgUser";
    public const string ExternalNotificationQmsgBot = "ExternalNotificationQmsgBot";
    public const string ExternalNotificationOneBotServer = "ExternalNotificationOneBotServer";
    public const string ExternalNotificationOneBotKey = "ExternalNotificationOneBotKey";
    public const string ExternalNotificationOneBotUser = "ExternalNotificationOneBotUser";
    
    #endregion

    #region 更新

    public const string UIUpdateChannelIndex = "UIUpdateChannelIndex";
    public const string ResourceUpdateChannelIndex = "ResourceUpdateChannelIndex";
    public const string DownloadSourceIndex = "DownloadSourceIndex";
    public const string EnableAutoUpdateResource = "EnableAutoUpdateResource";
    public const string EnableAutoUpdateMFA = "EnableAutoUpdateMFA";
    public const string EnableCheckVersion = "EnableCheckVersion";
    public const string DownloadCDK = "DownloadCDK";
    public const string GitHubToken = "GitHubToken";
    public const string ProxyAddress  = "ProxyAddress";   
    public const string ProxyType = "ProxyType";
    public const string CurrentTasks = "CurrentTasks";
    
    #endregion

    #region UI设置
    
    public const string TaskQueueColumn1Width = "UI.TaskQueue.Column1Width";
    public const string TaskQueueColumn2Width = "UI.TaskQueue.Column2Width";
    public const string TaskQueueColumn3Width = "UI.TaskQueue.Column3Width";
    public const string MainWindowWidth = "UI.MainWindow.Width";
    public const string MainWindowHeight = "UI.MainWindow.Height";
    
    #endregion
    
}
