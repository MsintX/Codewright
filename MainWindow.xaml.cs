using Codewright.Models;
using Codewright.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Codewright;

public sealed partial class MainWindow : Window
{
    private readonly GitService _git = new();
    private readonly AppSettingsService _settingsService = new();
    private readonly ObservableCollection<GitChange> _changes = new();
    private readonly ObservableCollection<GitCommit> _commits = new();
    private AppSettings _settings = new();
    private string? _workspacePath;
    private string? _repoPath;
    private readonly TaskCompletionSource<bool> _contentReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MainWindow()
    {
        InitializeComponent();
        ChangesList.ItemsSource = _changes;
        LogList.ItemsSource = _commits;
        RootGrid.Loaded += RootGrid_Loaded;
    }

    private void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (RootGrid.XamlRoot is not null)
            _contentReady.TrySetResult(true);
    }

    public async Task InitializeAsync()
    {
        _settings = _settingsService.Load();

        if (RootGrid.XamlRoot is null)
            await _contentReady.Task;

        string? workspacePath = null;
        if (_settings.UseSavedWorkDirectory && Directory.Exists(_settings.WorkDirectory))
            workspacePath = _settings.WorkDirectory;
        else
            workspacePath = await ShowWorkDirectoryDialogAsync();

        if (workspacePath is null)
        {
            Close();
            return;
        }

        _workspacePath = workspacePath;

        if (await _git.IsGitRepositoryAsync(_workspacePath))
        {
            _repoPath = _workspacePath;
            await RefreshRepositoryAsync();
        }
        else
        {
            RepoPathBox.Text = _workspacePath;
            BranchText.Text = "尚未连接仓库";
            RepoSummaryText.Text = "当前只有工作目录；连接 GitHub 后即可使用拉取、推送和提交并推送。";
        }

        await ShowGitHubRepositoryDialogAsync(requiredConnection: false);
    }

    private async Task<string?> ShowWorkDirectoryDialogAsync()
    {
        var directoryBox = new TextBox
        {
            PlaceholderText = "例如：E:\\SourceFiles\\repo",
            Text = _settings.UseSavedWorkDirectory && Directory.Exists(_settings.WorkDirectory)
                ? _settings.WorkDirectory
                : string.Empty,
            MinWidth = 480
        };

        var pickButton = new Button { Content = "选择文件夹" };
        pickButton.Click += async (_, _) =>
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
                directoryBox.Text = folder.Path;
        };

        var alwaysUseCheckBox = new CheckBox
        {
            Content = "以后都是这个目录",
            IsChecked = _settings.UseSavedWorkDirectory
        };

        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(new TextBlock
        {
            Text = "Codewright 先确认你的工作目录。这个目录会作为本地仓库的工作空间。",
            TextWrapping = TextWrapping.Wrap
        });
        var pathRow = new Grid { ColumnSpacing = 10 };
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathRow.Children.Add(directoryBox);
        Grid.SetColumn(pickButton, 1);
        pathRow.Children.Add(pickButton);
        content.Children.Add(pathRow);
        content.Children.Add(alwaysUseCheckBox);

        var dialog = new ContentDialog
        {
            Title = "选择工作目录",
            Content = content,
            PrimaryButtonText = "继续",
            CloseButtonText = "退出",
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = RootGrid.XamlRoot
        };

        while (true)
        {
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return null;

            var path = directoryBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(path))
            {
                await ShowMessageAsync("请选择一个工作目录。", "目录不能为空");
                continue;
            }

            if (!Directory.Exists(path))
            {
                var create = new ContentDialog
                {
                    Title = "目录不存在",
                    Content = $"目录“{path}”还不存在，要创建它吗？",
                    PrimaryButtonText = "创建并继续",
                    CloseButtonText = "返回",
                    DefaultButton = ContentDialogButton.Primary,
                    Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                    XamlRoot = RootGrid.XamlRoot
                };

                if (await create.ShowAsync() != ContentDialogResult.Primary)
                    continue;

                Directory.CreateDirectory(path);
            }

            _settings.UseSavedWorkDirectory = alwaysUseCheckBox.IsChecked == true;
            _settings.WorkDirectory = path;
            _settingsService.Save(_settings);
            return path;
        }
    }

    private async Task<bool> ShowGitHubRepositoryDialogAsync(bool requiredConnection)
    {
        if (RootGrid.XamlRoot is null)
            return false;

        var urlBox = new TextBox
        {
            PlaceholderText = "https://github.com/username/repository.git",
            Text = _settings.LastGitHubUrl ?? string.Empty,
            MinWidth = 560
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = requiredConnection
                ? "该操作需要远程仓库。输入 GitHub 仓库的 .git 地址后，Codewright 会连接当前工作区。"
                : "输入 GitHub 仓库的 .git 地址进行连接；也可以先跳过，之后需要拉取或推送时再连接。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(urlBox);
        content.Children.Add(new TextBlock
        {
            Text = "示例：https://github.com/MsintX/MsixBuilder.git",
            Opacity = 0.6,
            FontSize = 12
        });

        var dialog = new ContentDialog
        {
            Title = "连接 GitHub 仓库",
            Content = content,
            PrimaryButtonText = "连接",
            DefaultButton = ContentDialogButton.Primary,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = RootGrid.XamlRoot
        };

        if (requiredConnection)
            dialog.CloseButtonText = "取消";
        else
            dialog.SecondaryButtonText = "暂不连接";

        while (true)
        {
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return false;

            var url = urlBox.Text.Trim();
            if (!IsSupportedGitHubUrl(url))
            {
                await ShowMessageAsync("请输入有效的 GitHub HTTPS 或 SSH .git 地址。", "仓库地址不正确");
                continue;
            }

            try
            {
                dialog.IsPrimaryButtonEnabled = false;
                dialog.IsSecondaryButtonEnabled = false;
                dialog.IsSecondaryButtonEnabled = !requiredConnection;
                await PrepareRepositoryAsync(url);
                _settings.LastGitHubUrl = url;
                _settingsService.Save(_settings);
                return true;
            }
            catch (Exception ex)
            {
                await ShowMessageAsync(ex.Message, "仓库连接失败");
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = !requiredConnection;
            }
        }
    }

    private async Task PrepareRepositoryAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(_workspacePath))
            throw new InvalidOperationException("工作目录尚未确定。");

        if (await _git.IsGitRepositoryAsync(_workspacePath))
        {
            var currentRemote = await _git.GetRemoteUrlAsync(_workspacePath);
            if (string.IsNullOrWhiteSpace(currentRemote))
                await _git.AddOriginAsync(_workspacePath, url);
            else if (!string.Equals(NormalizeRemote(currentRemote), NormalizeRemote(url), StringComparison.OrdinalIgnoreCase))
                await _git.SetOriginUrlAsync(_workspacePath, url);

            _repoPath = _workspacePath;
            await RefreshRepositoryAsync();
            ShowStatus("GitHub 仓库已连接。", false);
            return;
        }

        var entries = Directory.Exists(_workspacePath)
            ? Directory.EnumerateFileSystemEntries(_workspacePath).ToList()
            : new List<string>();

        string destination;
        if (entries.Count == 0)
        {
            destination = _workspacePath;
        }
        else
        {
            var repoName = GetRepositoryFolderName(url);
            destination = Path.Combine(_workspacePath, repoName);
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
                throw new InvalidOperationException($"目标目录“{destination}”已经存在且不为空，请选择空目录或清理后再试。");
        }

        await _git.CloneAsync(url, destination);
        _repoPath = destination;
        RepoPathBox.Text = destination;
        await RefreshRepositoryAsync();
        ShowStatus("GitHub 仓库已克隆。", false);
    }

    private static bool IsSupportedGitHubUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            return false;

        if (url.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
               (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("git", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRepositoryFolderName(string url)
    {
        var trimmed = url.TrimEnd('/');
        var lastSegment = trimmed.Split('/', ':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "repository.git";
        return lastSegment.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? lastSegment[..^4]
            : lastSegment;
    }

    private static string NormalizeRemote(string value)
        => value.Trim().TrimEnd('/').Replace("git@github.com:", "https://github.com/", StringComparison.OrdinalIgnoreCase);

    private async Task ShowMessageAsync(string message, string title)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
            CloseButtonText = "知道了",
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = RootGrid.XamlRoot
        };
        await dialog.ShowAsync();
    }

    public async Task ShowStartupErrorAsync(Exception ex)
    {
        if (RootGrid.XamlRoot is null)
            return;

        var dialog = new ContentDialog
        {
            Title = "Codewright 启动失败",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = ex.ToString(),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            },
            CloseButtonText = "退出",
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = RootGrid.XamlRoot
        };

        await dialog.ShowAsync();
        Close();
    }

    private async void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        await OpenRepositoryAsync(folder.Path);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshRepositoryAsync();

    private async void BranchManager_Click(object sender, RoutedEventArgs e) => await ShowBranchManagerAsync();

    private async void Pull_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureRemoteConnectionAsync()) return;
        await RunGitActionAsync("git pull", () => _git.PullAsync(_repoPath!));
    }

    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureRemoteConnectionAsync()) return;
        await RunGitActionAsync("git push", () => _git.PushAsync(_repoPath!));
    }

    private async void CommitPush_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureRemoteConnectionAsync()) return;
        await CommitAndPushAsync();
    }

    private async Task CommitAndPushAsync()
    {
        if (!EnsureRepository()) return;

        var message = CommitMessageBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            ShowStatus("请先输入提交信息。", true);
            return;
        }

        await RunGitActionAsync("git commit + push", async () =>
        {
            await _git.AddAllAsync(_repoPath!);
            var output = await _git.CommitAsync(_repoPath!, message);
            output += "\n" + await _git.PushAsync(_repoPath!);
            return output;
        });

        CommitMessageBox.Text = string.Empty;
    }

    private async Task<bool> EnsureRemoteConnectionAsync()
    {
        if (!EnsureRepository())
            return false;

        var remote = await _git.GetRemoteUrlAsync(_repoPath!);
        if (!string.IsNullOrWhiteSpace(remote))
            return true;

        return await ShowGitHubRepositoryDialogAsync(requiredConnection: true);
    }

    private async Task OpenRepositoryAsync(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                throw new InvalidOperationException("目录不存在。\n" + path);
            if (!await _git.IsGitRepositoryAsync(path))
                throw new InvalidOperationException("这个目录不是 Git 仓库。请选择包含 .git 的项目目录。");

            _repoPath = path;
            _workspacePath = path;
            await RefreshRepositoryAsync();
            ShowStatus("本地仓库已打开。", false);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, true);
        }
    }

    private async Task ShowBranchManagerAsync()
    {
        if (!EnsureRepository()) return;

        var branches = new ObservableCollection<GitBranch>();
        var branchList = new ListView
        {
            Height = 240,
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = nameof(GitBranch.Name),
            ItemsSource = branches
        };

        var nameBox = new TextBox
        {
            PlaceholderText = "输入分支名称…",
            MinWidth = 280
        };

        var forceDeleteCheckBox = new CheckBox
        {
            Content = "强制删除未合并分支"
        };

        var managerStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        };

        var addButton = new Button { Content = "添加分支" };
        var renameButton = new Button { Content = "重命名" };
        var deleteButton = new Button { Content = "删除" };
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttonRow.Children.Add(addButton);
        buttonRow.Children.Add(renameButton);
        buttonRow.Children.Add(deleteButton);

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "这里只管理本地分支。当前分支会在列表中显示，删除使用 Git 的安全删除规则，也可以选择强制删除。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(branchList);
        content.Children.Add(nameBox);
        content.Children.Add(buttonRow);
        content.Children.Add(forceDeleteCheckBox);
        content.Children.Add(managerStatus);

        var dialog = new ContentDialog
        {
            Title = "本地分支管理",
            Content = content,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
            XamlRoot = RootGrid.XamlRoot
        };

        async Task ReloadBranchesAsync(string? selectName = null)
        {
            branches.Clear();
            foreach (var branch in await _git.GetLocalBranchesAsync(_repoPath!))
                branches.Add(branch);

            if (selectName is not null)
            {
                var item = branches.FirstOrDefault(b => string.Equals(b.Name, selectName, StringComparison.Ordinal));
                if (item is not null)
                    branchList.SelectedItem = item;
            }
            else if (branches.FirstOrDefault(b => b.IsCurrent) is { } current)
            {
                branchList.SelectedItem = current;
            }
        }

        await ReloadBranchesAsync();

        addButton.Click += async (_, _) =>
        {
            var name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                managerStatus.Text = "请输入新分支名称。";
                return;
            }

            try
            {
                SetBranchControlsEnabled(false);
                await _git.CreateBranchAsync(_repoPath!, name);
                nameBox.Text = string.Empty;
                await ReloadBranchesAsync(name);
                await RefreshRepositoryAsync();
                managerStatus.Text = $"已添加本地分支“{name}”。";
            }
            catch (Exception ex)
            {
                managerStatus.Text = ex.Message.Trim();
            }
            finally
            {
                SetBranchControlsEnabled(true);
            }
        };

        renameButton.Click += async (_, _) =>
        {
            if (branchList.SelectedItem is not GitBranch selected)
            {
                managerStatus.Text = "先选择一个要重命名的分支。";
                return;
            }

            var newName = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                managerStatus.Text = "请输入新的分支名称。";
                return;
            }

            try
            {
                SetBranchControlsEnabled(false);
                await _git.RenameBranchAsync(_repoPath!, selected.Name, newName);
                nameBox.Text = string.Empty;
                await ReloadBranchesAsync(newName);
                await RefreshRepositoryAsync();
                managerStatus.Text = $"已将“{selected.Name}”重命名为“{newName}”。";
            }
            catch (Exception ex)
            {
                managerStatus.Text = ex.Message.Trim();
            }
            finally
            {
                SetBranchControlsEnabled(true);
            }
        };

        deleteButton.Click += async (_, _) =>
        {
            if (branchList.SelectedItem is not GitBranch selected)
            {
                managerStatus.Text = "先选择一个要删除的分支。";
                return;
            }

            var force = forceDeleteCheckBox.IsChecked == true;
            try
            {
                SetBranchControlsEnabled(false);
                await _git.DeleteBranchAsync(_repoPath!, selected.Name, force);
                nameBox.Text = string.Empty;
                await ReloadBranchesAsync();
                await RefreshRepositoryAsync();
                managerStatus.Text = $"已删除本地分支“{selected.Name}”。";
            }
            catch (Exception ex)
            {
                managerStatus.Text = ex.Message.Trim();
            }
            finally
            {
                SetBranchControlsEnabled(true);
            }
        };

        await dialog.ShowAsync();

        void SetBranchControlsEnabled(bool enabled)
        {
            addButton.IsEnabled = enabled;
            renameButton.IsEnabled = enabled;
            deleteButton.IsEnabled = enabled;
            branchList.IsEnabled = enabled;
            nameBox.IsEnabled = enabled;
            forceDeleteCheckBox.IsEnabled = enabled;
        }
    }

    private async Task RefreshRepositoryAsync()
    {
        if (!EnsureRepository()) return;
        try
        {
            var branch = await _git.GetCurrentBranchAsync(_repoPath!);
            var changes = await _git.GetStatusAsync(_repoPath!);
            var commits = await _git.GetLogAsync(_repoPath!, 20);
            var remote = await _git.GetRemoteUrlAsync(_repoPath!);

            BranchText.Text = string.IsNullOrWhiteSpace(branch) ? "HEAD 分离" : branch;
            RepoPathBox.Text = _repoPath;
            RepoSummaryText.Text = string.IsNullOrWhiteSpace(remote)
                ? $"{changes.Count} 个工作区变更 · 未配置远程"
                : $"{changes.Count} 个工作区变更 · {remote}";

            _changes.Clear();
            foreach (var change in changes) _changes.Add(change);
            ChangesEmptyText.Visibility = changes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            _commits.Clear();
            foreach (var commit in commits) _commits.Add(commit);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, true);
        }
    }

    private async Task RunGitActionAsync(string action, Func<Task<string>> operation)
    {
        if (!EnsureRepository()) return;
        try
        {
            var output = await operation();
            await RefreshRepositoryAsync();
            ShowStatus(string.IsNullOrWhiteSpace(output) ? $"{action} 完成。" : output.Trim(), false);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, true);
        }
    }

    private bool EnsureRepository()
    {
        if (!string.IsNullOrWhiteSpace(_repoPath)) return true;
        ShowStatus("当前还没有打开本地 Git 仓库。请先连接 GitHub 或用“打开文件夹”打开一个仓库。", true);
        return false;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusBar.Severity = isError
            ? InfoBarSeverity.Error
            : InfoBarSeverity.Success;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
