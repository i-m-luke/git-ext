// git-ext: a .NET 10 file-based app that adds custom "git ext <command>" subcommands.
// Run via git-ext.bat (placed on PATH) so it is invoked as: git ext <command> <args>
//
// Design:
//   - Commands are registered in a dictionary keyed by name (ICommand registry).
//   - git is invoked directly through Process (one command per call); the "&&"
//     semantics are reproduced by checking each exit code in C#.
//   - git's stdout/stderr are inherited (pass-through) so the user sees git's
//     native messages; on failure we add a one-line "git ext <cmd>:" prefix and
//     propagate git's exit code.

using System.Diagnostics;

// ── entry point (top-level statements) ──────────────────────────────────────
if (!GitRunner.IsGitAvailable())
{
    Console.Error.WriteLine("git ext: 'git' was not found on PATH. Install git and try again.");
    return 1;
}

var commands = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase)
{
    ["sync"] = new SyncCommand(),
};

if (args.Length == 0)
{
    Console.Error.WriteLine($"git ext: no command given. Available: {string.Join(", ", commands.Keys)}");
    return 1;
}

if (!commands.TryGetValue(args[0], out var command))
{
    Console.Error.WriteLine($"git ext: unknown command '{args[0]}'. Available: {string.Join(", ", commands.Keys)}");
    return 1;
}

return command.Execute(args[1..]);

// ── contracts ───────────────────────────────────────────────────────────────
interface ICommand
{
    int Execute(string[] args);
}

// ── commands ──────────────────────────────────────────────────────────────--
// sync <source> <merge-from> <new-branch>
//   1. create <new-branch> from <source>   (git switch -c <new-branch> <source>)
//   2. merge <merge-from> into it          (git merge <merge-from>)
//   Result: <new-branch> checked out, containing <merge-from> merged in.
sealed class SyncCommand : ICommand
{
    public int Execute(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: git ext sync <source> <merge-from> <new-branch>");
            return 1;
        }

        var source = args[0];
        var mergeFrom = args[1];
        var newBranch = args[2];

        var switchExit = GitRunner.Run("switch", "-c", newBranch, source);
        if (switchExit != 0)
        {
            Console.Error.WriteLine($"git ext sync: failed to create '{newBranch}' from '{source}'.");
            return switchExit;
        }

        var mergeExit = GitRunner.Run("merge", mergeFrom);
        if (mergeExit != 0)
        {
            Console.Error.WriteLine(
                $"git ext sync: merging '{mergeFrom}' into '{newBranch}' did not complete. " +
                "If there are conflicts, resolve them and run 'git commit' to finish the merge.");
            return mergeExit;
        }

        return 0;
    }
}

// ── git process helper ────────────────────────────────────────────────────--
static class GitRunner
{
    public static bool IsGitAvailable()
    {
        try
        {
            // Silent probe: redirect output so the version banner is not shown.
            var psi = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("--version");

            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // Runs "git <arguments...>" inheriting the current console (pass-through),
    // so git's own stdout/stderr reach the user. Returns git's exit code.
    public static int Run(params string[] arguments)
    {
        var psi = new ProcessStartInfo("git") { UseShellExecute = false };
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start the git process.");
        process.WaitForExit();
        return process.ExitCode;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Alternative prototype — option C: System.CommandLine (kept for reference).
// Pulls in a NuGet package via the #:package directive and gives auto-generated
// --help, argument validation and usage errors for free, at the cost of a
// dependency and more wiring. Not used; we chose the lightweight registry above.
//
//   #:package System.CommandLine@2.0.0-beta4.22272.1
//   using System.CommandLine;
//   using System.Diagnostics;
//
//   if (!GitRunner.IsGitAvailable())
//   {
//       Console.Error.WriteLine("git ext: 'git' was not found on PATH.");
//       return 1;
//   }
//
//   var root = new RootCommand("git-ext: git extension commands");
//
//   var sourceArg    = new Argument<string>("source",     "branch to base the new branch on");
//   var mergeFromArg = new Argument<string>("merge-from", "branch to merge in");
//   var newBranchArg = new Argument<string>("new-branch", "name of the resulting branch");
//
//   var sync = new Command("sync", "create <new-branch> from <source>, then merge <merge-from> into it")
//   {
//       sourceArg, mergeFromArg, newBranchArg,
//   };
//
//   sync.SetHandler((source, mergeFrom, newBranch) =>
//   {
//       if (GitRunner.Run("switch", "-c", newBranch, source) != 0)
//           return 1;
//       return GitRunner.Run("merge", mergeFrom);
//   }, sourceArg, mergeFromArg, newBranchArg);
//
//   root.AddCommand(sync);
//   return root.Invoke(args);
// ─────────────────────────────────────────────────────────────────────────────
