using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using STranslate.Core;
using STranslate.Plugin;
using System.Diagnostics;
using System.IO;
using System.Net;
using WebDav;

namespace STranslate.ViewModels.Pages;

public partial class AboutViewModel(
    Settings settings,
    DataProvider dataProvider,
    ISnackbar snackbar,
    INotification notification,
    Internationalization i18n,
    ILogger<AboutViewModel> logger) : ObservableObject
{
    public Settings Settings { get; } = settings;
    public DataProvider DataProvider { get; } = dataProvider;
    [ObservableProperty] public partial string AppVersion { get; set; } = VersionInfo.GetVersion();

    #region ICommand

    [RelayCommand]
    private void LocateUserData()
    {
        var settingsFolderPath = Path.Combine(DataLocation.SettingsDirectory);
        var parentFolderPath = Path.GetDirectoryName(settingsFolderPath);
        if (Directory.Exists(parentFolderPath))
        {
            Process.Start("explorer.exe", parentFolderPath);
        }
    }

    [RelayCommand]
    private void LocateLog()
    {
        var logFolderPath = Path.Combine(Constant.LogDirectory);
        if (Directory.Exists(logFolderPath))
        {
            Process.Start("explorer.exe", logFolderPath);
        }
    }

    [RelayCommand]
    private void LocateSettings()
    {
        var settingsFolderPath = Path.Combine(DataLocation.SettingsDirectory);
        if (Directory.Exists(settingsFolderPath))
        {
            Process.Start("explorer.exe", settingsFolderPath);
        }
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        if (Settings.Backup.Type == BackupType.Local)
            LocalBackup();
        else
            await PreWebDavBackupAsync();
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (Settings.Backup.Type == BackupType.Local)
            LocalRestore();
        else
            await PreWebDavRestoreAsync();
    }

    #endregion

    #region Local Backup

    private void LocalBackup()
    {
        var saveFileDialog = new SaveFileDialog
        {
            Title = "Select Backup File",
            Filter = "zip(*.zip)|*.zip",
            FileName = $"stranslate_backup_{DateTime.Now:yyyyMMddHHmmss}"
        };

        if (saveFileDialog.ShowDialog() != true)
            return;

        var filePath = saveFileDialog.FileName;
        string[] args = [
            "backup",
            "-m", "backup",
            "-a", filePath,
            "-f", DataLocation.PluginsDirectory,
            "-f", DataLocation.SettingsDirectory,
            "-d", "3",
            "-l", DataLocation.AppExePath,
            "-c", DataLocation.InfoFilePath,
            "-w", $"备份配置成功 [{filePath}]"
            ];
        Utilities.ExecuteProgram(DataLocation.HostExePath, args);
        App.Current.Shutdown();
    }

    private void LocalRestore()
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select Restore File",
            Filter = "zip(*.zip)|*.zip"
        };
        if (openFileDialog.ShowDialog() != true)
            return;
        var filePath = openFileDialog.FileName;
        string[] args = [
            "backup", "-m",
            "restore", "-a",
            filePath, "-s", Constant.Plugins,
            "-t", DataLocation.PluginsDirectory,
            "-s", Constant.Settings,
            "-t", DataLocation.SettingsDirectory,
            "-d", "3",
            "-l", DataLocation.AppExePath,
            "-c", DataLocation.InfoFilePath,
            "-w", $"恢复配置成功 [{filePath}]"
        ];
        Utilities.ExecuteProgram(DataLocation.HostExePath, args);
        App.Current.Shutdown();
    }

    #endregion

    #region WebDav Backup

    private async Task PreWebDavBackupAsync()
    {
        // 测试连接是否成功
        var result = await CreateClientAsync();
        if (!result.isSucess)
        {
            snackbar.Show("请检查配置或查看日志");
            logger.LogError($"Backup|CreateClientAsync|Failed Message: {result.message}");
            return;
        }

        var fileName = $"stranslate_backup_{DateTime.Now:yyyyMMddHHmmss}.zip";
        var filePath = Path.Combine(Constant.ProgramDirectory, fileName);
        string[] args = [
            "backup",
            "-m", "backup",
            "-a", filePath,
            "-f", DataLocation.PluginsDirectory,
            "-f", DataLocation.SettingsDirectory,
            "-d", "3",
            "-l", DataLocation.AppExePath,
            "-c", DataLocation.BackupFilePath,
            "-w", filePath
            ];
        Utilities.ExecuteProgram(DataLocation.HostExePath, args);
        App.Current.Shutdown();
    }

    public async Task PostWebDavBackupAsync(string filePath)
    {
        var result = await CreateClientAsync();
        if (!result.isSucess)
        {
            notification.Show(i18n.GetTranslation("Toast"), "请检查配置或查看日志");
            logger.LogError($"Backup|CreateClientAsync|Failed Message: {result.message}");
            return;
        }

        var client = result.client;
        var absolutePath = result.message;

        var fileName = Path.GetFileName(filePath);

        try
        {
            // 检查该路径是否存在
            var ret = await client.Propfind(absolutePath);
            if (!ret.IsSuccessful)
                // 不存在则创建目录
                await client.Mkcol(absolutePath);

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var response = await client.PutFile($"{absolutePath}{fileName}", fileStream);

            // 打印通知
            if (response.IsSuccessful && response.StatusCode == 201)
                notification.Show(i18n.GetTranslation("Toast"), "备份成功");
            else
            {
                notification.Show(i18n.GetTranslation("Toast"), "备份失败，请检查日志");
                logger.LogError($"Backup|PutFile|Error Code: {response.StatusCode} Description: {response.Description}");
            }
        }
        finally
        {
            try
            {
                File.Delete(filePath);
            }
            catch { }
        }
    }

    private async Task PreWebDavRestoreAsync()
    {

    }

    /// <summary>
    ///     创建WebDavClient
    /// </summary>
    /// <returns></returns>
    private async Task<(bool isSucess, WebDavClient client, string message)> CreateClientAsync()
    {
        // 如果没有/结尾则添加
        // * TeraCloud强制需要以/结尾
        // * 群晖、坚果云有无均可
        var uri = new Uri(Settings.Backup.Address.EndsWith('/') ? Settings.Backup.Address : $"{Settings.Backup.Address}/");

        // TeraCloud强制需要以/结尾
        // * 群晖、坚果云有无均可
        var absolutePath = $"{uri.LocalPath.TrimEnd('/')}/{Constant.AppName}/";

        var clientParams = new WebDavClientParams
        {
            Timeout = TimeSpan.FromSeconds(10),
            BaseAddress = uri,
            Credentials = new NetworkCredential(Settings.Backup.Username, Settings.Backup.Password)
        };
        var client = new WebDavClient(clientParams);
        try
        {
            var linkTest = await client.Propfind(string.Empty);
            var result = linkTest.IsSuccessful;
            return (result,
                result ? client : new WebDavClient(),
                result ? absolutePath : $"Code: {linkTest.StatusCode} Description: {linkTest.Description}");
        }
        catch (Exception ex)
        {
            return (false, new WebDavClient(), ex.Message);
        }
    }

    #endregion
}
