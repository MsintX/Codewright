using Codewright.Models;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Codewright.Services;

public sealed class GitService
{
    public async Task<bool> IsGitRepositoryAsync(string workingDirectory)
    {
        try
        {
            var result = await RunAsync(workingDirectory, "rev-parse", "--is-inside-work-tree");
            return result.ExitCode == 0 && result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetCurrentBranchAsync(string repo) =>
        (await RunCheckedAsync(repo, "branch", "--show-current")).Trim();

    public async Task<List<GitBranch>> GetLocalBranchesAsync(string repo)
    {
        var current = await GetCurrentBranchAsync(repo);
        var output = await RunCheckedAsync(repo, "branch", "--format=%(refname:short)");
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new GitBranch(name, string.Equals(name, current, StringComparison.Ordinal)))
            .ToList();
    }

    public Task<string> CreateBranchAsync(string repo, string name) =>
        RunCheckedAsync(repo, "branch", name);

    public Task<string> RenameBranchAsync(string repo, string oldName, string newName) =>
        RunCheckedAsync(repo, "branch", "-m", oldName, newName);

    public Task<string> DeleteBranchAsync(string repo, string name, bool force = false) =>
        force
            ? RunCheckedAsync(repo, "branch", "-D", name)
            : RunCheckedAsync(repo, "branch", "-d", name);

    public async Task<List<GitChange>> GetStatusAsync(string repo)
    {
        var output = await RunCheckedAsync(repo, "status", "--porcelain=v1");
        var result = new List<GitChange>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 4) continue;
            var code = line[..2].Trim();
            var path = line[3..].Trim();
            var status = code switch
            {
                "M" or "MM" => "M",
                "A" or "AA" => "A",
                "D" or "DD" => "D",
                "R" or "RR" => "R",
                "??" => "U",
                _ => string.IsNullOrWhiteSpace(code) ? "?" : code
            };
            result.Add(new GitChange(status, path));
        }
        return result;
    }

    public async Task<List<GitCommit>> GetLogAsync(string repo, int count)
    {
        var format = "%h%x1f%an%x1f%ad%x1f%s%x1e";
        var output = await RunCheckedAsync(repo, "log", $"-{count}", "--date=short", $"--pretty=format:{format}");
        var result = new List<GitCommit>();
        foreach (var record in output.Split('\x1e', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = record.Trim('\n').Split('\x1f');
            if (parts.Length >= 4)
                result.Add(new GitCommit(parts[0], parts[1], parts[2], parts[3]));
        }
        return result;
    }

    public async Task<string> GetRemoteUrlAsync(string repo)
    {
        var result = await RunAsync(repo, "remote", "get-url", "origin");
        return result.ExitCode == 0 ? result.StdOut.Trim() : string.Empty;
    }

    public async Task CloneAsync(string url, string destinationDirectory)
    {
        var parent = Directory.GetParent(destinationDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
            throw new InvalidOperationException("无法确定仓库目标目录。");

        Directory.CreateDirectory(parent);
        await RunCheckedAsync(parent, "clone", url, destinationDirectory);
    }

    public Task<string> AddOriginAsync(string repo, string url) =>
        RunCheckedAsync(repo, "remote", "add", "origin", url);

    public Task<string> SetOriginUrlAsync(string repo, string url) =>
        RunCheckedAsync(repo, "remote", "set-url", "origin", url);

    public Task<string> AddAllAsync(string repo) => RunCheckedAsync(repo, "add", "-A");
    public Task<string> CommitAsync(string repo, string message) => RunCheckedAsync(repo, "commit", "-m", message);
    public Task<string> PullAsync(string repo) => RunCheckedAsync(repo, "pull");
    public Task<string> PushAsync(string repo) => RunCheckedAsync(repo, "push");

    private static async Task<string> RunCheckedAsync(string workingDirectory, params string[] arguments)
    {
        var result = await RunAsync(workingDirectory, arguments);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr);
        return result.StdOut;
    }

    private static async Task<GitCommandResult> RunAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git.exe",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("无法启动 git.exe，请先安装 Git for Windows 并加入 PATH。\nhttps://git-scm.com/download/win");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("找不到 git.exe。请先安装 Git for Windows 并把 Git 加入 PATH。", ex);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask);
        return new GitCommandResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private sealed record GitCommandResult(int ExitCode, string StdOut, string StdErr);
}
