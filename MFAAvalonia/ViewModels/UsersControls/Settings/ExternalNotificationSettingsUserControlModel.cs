using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class ExternalNotificationSettingsUserControlModel : ViewModelBase
{
    #region 初始

    protected override void Initialize()
    {
        UpdateExternalNotificationProvider();
    }

    public static readonly List<LocalizationViewModel> ExternalNotificationProviders = ExternalNotificationHelper.Key.AllKeys.Select(k => new LocalizationViewModel(k)).ToList();

    public void UpdateExternalNotificationProvider()
    {
        DingTalkEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.DingTalkKey);
        EmailEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.EmailKey);
        LarkEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.LarkKey);
        QmsgEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.QmsgKey);
        WxPusherEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.WxPusherKey);
        SmtpEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.SmtpKey);
        TelegramEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.TelegramKey);
        DiscordEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.DiscordKey);
        DiscordWebhookEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.DiscordWebhookKey);
        OnebotEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.OneBotKey);
        ServerChanEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.ServerChanKey);
        CustomWebhookEnabled = EnabledExternalNotificationProviderList.Contains(ExternalNotificationHelper.Key.CustomWebhookKey);
    }

    public static readonly List<string> EnabledExternalNotificationProviderList = ExternalNotificationProviders
        .Where(s => ConfigurationManager.Current.GetValue(ConfigurationKeys.ExternalNotificationEnabled, string.Empty).Split(',').Contains(s.ResourceKey))
        .Distinct().Select(t => t.ResourceKey)
        .ToList();

    public void UpdateEnabledExternalNotificationProviderList(string key, bool value)
    {
        var exists = EnabledExternalNotificationProviderList.Contains(key);

        if (value && !exists)
        {
            EnabledExternalNotificationProviderList.Add(key);
        }
        else if (!value && exists)
        {
            EnabledExternalNotificationProviderList.RemoveAll(s => s == key);
        }

        try
        {
            var config = string.Join(",", EnabledExternalNotificationProviderList.Distinct());
            ConfigurationManager.Current.SetValue(ConfigurationKeys.ExternalNotificationEnabled, config);
            EnabledExternalNotificationProviderCount = EnabledExternalNotificationProviderList.Count;
        }
        catch (Exception e)
        {
            LoggerHelper.Error(e);
        }
    }

    [ObservableProperty] private int _enabledExternalNotificationProviderCount = EnabledExternalNotificationProviderList.Count;

#pragma warning disable CS4014 // 由于等待不会停止
    [RelayCommand]
    private void ExternalNotificationSendTest()
        => ExternalNotificationHelper.ExternalNotificationAsync(LangKeys.ExternalNotificationTest.ToLocalization());

    [ObservableProperty] private bool _enabledCustom = ConfigurationManager.Current.GetValue(ConfigurationKeys.ExternalNotificationEnableCustomMessage, false);
    partial void OnEnabledCustomChanged(bool value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationEnableCustomMessage, value);

    [ObservableProperty] private string _customSuccessText = ConfigurationManager.Current.GetValue(ConfigurationKeys.ExternalNotificationCustomSuccessText, string.Empty);
    partial void OnCustomSuccessTextChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationCustomSuccessText, value);

    [ObservableProperty] private string _customFailureText = ConfigurationManager.Current.GetValue(ConfigurationKeys.ExternalNotificationCustomFailureText, string.Empty);
    partial void OnCustomFailureTextChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationCustomFailureText, value);

    #endregion

    #region 钉钉

    [ObservableProperty] private bool _dingTalkEnabled;
    partial void OnDingTalkEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.DingTalkKey, value);

    [ObservableProperty] private string _dingTalkToken = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationDingTalkToken, string.Empty);

    private static bool TryExtractDingTalkToken(string url, out string token)
    {
        token = string.Empty;

        try
        {
            if (string.IsNullOrWhiteSpace(url) && !url.Contains("access_token=", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var blocks = url.Split("access_token=");
            if (blocks.Length < 2)
            {
                return false;
            }

            token = blocks[1];
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    partial void OnDingTalkTokenChanged(string value)
    {
        if (TryExtractDingTalkToken(value, out var token))
            DingTalkToken = token;
        else
            ConfigurationManager.Current.SetValue(ConfigurationKeys.ExternalNotificationDingTalkToken, SimpleEncryptionHelper.Encrypt(value));
    }

    [ObservableProperty] private string _dingTalkSecret = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationDingTalkSecret, string.Empty);
    partial void OnDingTalkSecretChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationDingTalkSecret, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region 邮箱

    [ObservableProperty] private bool _emailEnabled;
    partial void OnEmailEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.EmailKey, value);

    [ObservableProperty] private string _emailAccount = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationEmailAccount, string.Empty);
    partial void OnEmailAccountChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationEmailAccount, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _emailSecret = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationEmailSecret, string.Empty);
    partial void OnEmailSecretChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationEmailSecret, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region 飞书

    [ObservableProperty] private bool _larkEnabled;

    partial void OnLarkEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.LarkKey, value);
    // [ObservableProperty] private bool _larkWebhookUsed;

    [ObservableProperty] private string _larkWebhookUrl = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationLarkWebhookUrl, string.Empty);

    partial void OnLarkWebhookUrlChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationLarkWebhookUrl, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _larkId = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationLarkID, string.Empty);

    partial void OnLarkIdChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationLarkID, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _larkToken = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationLarkToken, string.Empty);
    partial void OnLarkTokenChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationLarkToken, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region 微信公众号

    [ObservableProperty] private bool _wxPusherEnabled;

    partial void OnWxPusherEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.WxPusherKey, value);

    [ObservableProperty] private string _wxPusherToken = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationWxPusherToken, string.Empty);

    partial void OnWxPusherTokenChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationWxPusherToken, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _wxPusherUid = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationWxPusherUID, string.Empty);
    partial void OnWxPusherUidChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationWxPusherUID, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region Telegram

    [ObservableProperty] private bool _telegramEnabled;

    partial void OnTelegramEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.TelegramKey, value);

    [ObservableProperty] private string _telegramBotToken = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationTelegramBotToken, string.Empty);

    partial void OnTelegramBotTokenChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationTelegramBotToken, SimpleEncryptionHelper.Encrypt(value));


    [ObservableProperty] private string _telegramChatId = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationTelegramChatId, string.Empty);
    partial void OnTelegramChatIdChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationTelegramChatId, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region Discord

    [ObservableProperty] private bool _discordEnabled;

    partial void OnDiscordEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.DiscordKey, value);

    [ObservableProperty] private string _discordBotToken = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationDiscordBotToken, string.Empty);
    partial void OnDiscordBotTokenChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationDiscordBotToken, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _discordChannelId = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationDiscordChannelId, string.Empty);
    partial void OnDiscordChannelIdChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationDiscordChannelId, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region DiscordWebhook

    [ObservableProperty] private bool _discordWebhookEnabled;

    partial void OnDiscordWebhookEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.DiscordWebhookKey, value);

    [ObservableProperty] private string _discordWebhookUrl = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationDiscordWebhookUrl, string.Empty);
    partial void OnDiscordWebhookUrlChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationDiscordWebhookUrl, SimpleEncryptionHelper.Encrypt(value));


    [ObservableProperty] private string _discordWebhookName = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationDiscordWebhookName, string.Empty);
    partial void OnDiscordWebhookNameChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationDiscordWebhookName, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region SMTP

    [ObservableProperty] private bool _smtpEnabled;
    partial void OnSmtpEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.SmtpKey, value);

    [ObservableProperty] private string _smtpServer = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationSmtpServer, string.Empty);

    partial void OnSmtpServerChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationSmtpServer, SimpleEncryptionHelper.Encrypt(value));


    [ObservableProperty] private string _smtpPort = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationSmtpPort, string.Empty);

    partial void OnSmtpPortChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationSmtpPort, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _smtpUser = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationSmtpUser, string.Empty);
    partial void OnSmtpUserChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationSmtpUser, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _smtpPassword = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationSmtpPassword, string.Empty);
    partial void OnSmtpPasswordChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationSmtpPassword, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _smtpFrom = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationSmtpFrom, string.Empty);
    partial void OnSmtpFromChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationSmtpFrom, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _smtpTo = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationSmtpTo, string.Empty);
    partial void OnSmtpToChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationSmtpTo, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private bool _smtpUseSsl = ConfigurationManager.Current.GetValue(ConfigurationKeys.ExternalNotificationSmtpUseSsl, false);
    partial void OnSmtpUseSslChanged(bool value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationSmtpUseSsl, value);

    [ObservableProperty] private bool _smtpRequireAuthentication = ConfigurationManager.Current.GetValue(ConfigurationKeys.ExternalNotificationSmtpRequiresAuthentication, false);
    partial void OnSmtpRequireAuthenticationChanged(bool value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationSmtpRequiresAuthentication, value);

    #endregion

    #region QMsg

    [ObservableProperty] private bool _qmsgEnabled;
    partial void OnQmsgEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.QmsgKey, value);

    [ObservableProperty] private string _qmsgServer = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationQmsgServer, string.Empty);

    partial void OnQmsgServerChanged(string value)
        =>
            ConfigurationManager.Current.SetValue(ConfigurationKeys.ExternalNotificationQmsgServer, SimpleEncryptionHelper.Encrypt(value));


    [ObservableProperty] private string _qmsgKey = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationQmsgKey, string.Empty);

    partial void OnQmsgKeyChanged(string value)
        =>
            ConfigurationManager.Current.SetValue(ConfigurationKeys.ExternalNotificationQmsgKey, SimpleEncryptionHelper.Encrypt(value));


    [ObservableProperty] private string _qmsgUser = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationQmsgUser, string.Empty);
    partial void OnQmsgUserChanged(string value)
        =>
            ConfigurationManager.Current.SetValue(ConfigurationKeys.ExternalNotificationQmsgUser, SimpleEncryptionHelper.Encrypt(value));


    [ObservableProperty] private string _qmsgBot = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationQmsgBot, string.Empty);
    partial void OnQmsgBotChanged(string value)
        =>
            ConfigurationManager.Current.SetValue(ConfigurationKeys.ExternalNotificationQmsgBot, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region OneBot

    [ObservableProperty] private bool _onebotEnabled;
    partial void OnOnebotEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.OneBotKey, value);

    [ObservableProperty] private string _onebotServer = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationOneBotServer, string.Empty);

    partial void OnOnebotServerChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationOneBotServer, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _onebotKey = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationOneBotKey, string.Empty);

    partial void OnOnebotKeyChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationOneBotKey, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _onebotUser = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationOneBotUser, string.Empty);
    partial void OnOnebotUserChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationOneBotUser, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region ServerChan

    [ObservableProperty] private bool _serverChanEnabled;
    partial void OnServerChanEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.ServerChanKey, value);

    [ObservableProperty] private string _serverChanSendKey = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationServerChanKey, string.Empty);
    partial void OnServerChanSendKeyChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationServerChanKey, SimpleEncryptionHelper.Encrypt(value));

    #endregion

    #region CustomWebhook

    [ObservableProperty] private bool _customWebhookEnabled;
    partial void OnCustomWebhookEnabledChanged(bool value) => UpdateEnabledExternalNotificationProviderList(ExternalNotificationHelper.Key.CustomWebhookKey, value);

    [ObservableProperty] private string _customWebhookUrl = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationCustomWebhookUrl, string.Empty);
    partial void OnCustomWebhookUrlChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationCustomWebhookUrl, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _customWebhookContentType = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationCustomWebhookContentType, "application/json");
    partial void OnCustomWebhookContentTypeChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationCustomWebhookContentType, SimpleEncryptionHelper.Encrypt(value));

    [ObservableProperty] private string _customWebhookPayloadTemplate = ConfigurationManager.Current.GetDecrypt(ConfigurationKeys.ExternalNotificationCustomWebhookPayloadTemplate, "{\"message\": \"{message}\"}");
    partial void OnCustomWebhookPayloadTemplateChanged(string value) => HandlePropertyChanged(ConfigurationKeys.ExternalNotificationCustomWebhookPayloadTemplate, SimpleEncryptionHelper.Encrypt(value));

    #endregion
}
