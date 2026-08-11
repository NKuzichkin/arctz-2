using System;
using System.IO;

namespace ArctZ.Tests.Screenshots.Support;

public static class RepoRoot
{
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArctZ.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root (ArctZ.slnx) above '{AppContext.BaseDirectory}'.");
        }

        return dir.FullName;
    }
}
