namespace Codewright.Models;

public sealed record GitChange(string Status, string Path);
public sealed record GitCommit(string ShortHash, string Author, string Date, string Subject);
public sealed record GitBranch(string Name, bool IsCurrent);
