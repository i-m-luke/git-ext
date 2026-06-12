@echo off
rem git-ext launcher: invoked by git as `git ext <command> <args>`.
rem Self-locating via %~dp0 (this file's folder) so it works from any repo,
rem as long as this folder is on PATH. Everything after `--` is forwarded to
rem the program as args; git child processes inherit the caller's working dir.
dotnet run "%~dp0git-ext.cs" -- %*
