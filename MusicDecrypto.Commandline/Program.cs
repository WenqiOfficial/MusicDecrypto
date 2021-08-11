﻿using Mono.Options;
using MusicDecrypto.Library;
using MusicDecrypto.Library.Common;
using MusicDecrypto.Library.Vendor;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MusicDecrypto.Commandline
{
    public static class Program
    {
        private static ConsoleColor _pushColor;
        private static readonly HashSet<string> _extension
            = new() { ".ncm", ".tm2", ".tm6", ".qmc0", ".qmc3", ".bkcmp3", ".qmcogg", ".qmcflac", ".tkm", ".bkcflac", ".mflac", ".kgm", ".kgma", ".vpr", ".kwm", ".xm" };

        public static void Main(string[] args)
        {
            _pushColor = Console.ForegroundColor;
            AppDomain.CurrentDomain.ProcessExit += new EventHandler(OnProcessExit);

            // Register log event
            Logger.LogEvent += new Logger.LogHandler(Log);

            List<string> input;
            SearchOption search = SearchOption.TopDirectoryOnly;
            bool help = args.Length == 0;
            var options = new OptionSet
            {
                { "f|force-overwrite", "Overwrite existing files.", f => Decrypto.ForceOverwrite = f != null },
                { "n|renew-name", "Renew Hash-like names basing on metadata.", n => TencentDecrypto.RenewName = n != null },
                { "r|recursive", "Search files recursively.", r => { if (r != null) search = SearchOption.AllDirectories; } },
                { "x|extensive", "Extend range of extensions to be detected.", x => { if (x != null) _extension.UnionWith(new []{ ".mp3", ".m4a", ".wav", ".flac" }); } },
                { "o|output=", "Output directory", o => { if (o != null) Decrypto.Output = new DirectoryInfo(o); } },
                { "h|help", "Show help.", h => help = h != null },
            };

            try
            {
                input = options.Parse(args);

                if (help)
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(@"
Usage:
  MusicDecrypto [options] [<input>...]

Arguments:
  <input>    Input files/directories.

Options:");
                    options.WriteOptionDescriptions(Console.Out);
                    return;
                }

                if (Decrypto.Output?.Exists == false)
                {
                    Logger.Log("Ignore output directory which does not exist.", Decrypto.Output.FullName, LogLevel.Error);
                }

                // Search for files
                var files = new HashSet<FileInfo>(new FileInfoComparer());
                foreach (string item in input)
                {
                    if (Directory.Exists(item))
                    {
                        files.UnionWith(Directory.GetFiles(item, "*", search)
                                                 .Where(path => _extension.Contains(Path.GetExtension(path).ToLowerInvariant()))
                                                 .Select(path => new FileInfo(path)));
                    }
                    else if (File.Exists(item))
                    {
                        files.Add(new FileInfo(item));
                    }
                }

                if (files.Count == 0)
                {
                    Log("Found no supported file from specified path(s).", LogLevel.Error);
                    return;
                }

                // Decrypt and dump
                _ = Parallel.ForEach(files, file =>
                {
                    try
                    {
                        using Decrypto decrypto = file.Extension switch
                        {
                            ".ncm"    => new NetEaseDecrypto(file),
                            ".tm2" or ".tm6"
                                      => new TencentSimpleDecrypto(file, AudioTypes.Mp4),
                            ".qmc0" or ".qmc3" or ".bkcmp3"
                                      => new TencentStaticDecrypto(file, AudioTypes.Mpeg),
                            ".qmcogg" => new TencentStaticDecrypto(file, AudioTypes.Ogg),
                            ".tkm"    => new TencentStaticDecrypto(file, AudioTypes.Mp4),
                            ".qmcflac" or ".bkcflac"
                                      => new TencentStaticDecrypto(file, AudioTypes.Flac),
                            ".mflac"  => new TencentDynamicDecrypto(file, AudioTypes.Flac),
                            ".kwm"    => new KuwoDecrypto(file),
                            ".kgm" or ".kgma"
                                      => new KugouBasicDecrypto(file),
                            ".vpr"    => new KugouVprDecrypto(file),
                            ".xm"     => new XiamiDecrypto(file, AudioTypes.Undefined),
                            ".mp3"    => new XiamiDecrypto(file, AudioTypes.Mpeg),
                            ".m4a"    => new XiamiDecrypto(file, AudioTypes.Mp4),
                            ".wav"    => new XiamiDecrypto(file, AudioTypes.Wav),
                            ".flac"   => new XiamiDecrypto(file, AudioTypes.Flac),
                            _ => throw new DecryptoException("File has an unsupported extension.", file.FullName)
                        };

                        decrypto?.Dump();
                    }
                    catch (Exception e)
                    {
                        Log(e.ToString(), LogLevel.Fatal);
                    }
                });

                Log($"Program finished with {Decrypto.DumpCount}/{files.Count} files decrypted successfully.", LogLevel.Info);
            }
            catch (OptionException e)
            {
                Log(e.ToString(), LogLevel.Fatal);
            }
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            Console.ForegroundColor = _pushColor;
        }

        private static void Log(string message, LogLevel level)
        {
            Console.ForegroundColor = level switch
            {
                LogLevel.Info => ConsoleColor.White,
                LogLevel.Warn => ConsoleColor.Yellow,
                LogLevel.Error => ConsoleColor.Red,
                LogLevel.Fatal => ConsoleColor.DarkRed,
                _ => throw new ArgumentOutOfRangeException(nameof(level)),
            };
            Console.WriteLine(DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + '|'
                            + level.ToString().ToUpperInvariant() + '|' + message);
        }
    }

    internal class FileInfoComparer : IEqualityComparer<FileInfo>
    {
        public bool Equals(FileInfo x, FileInfo y) => Equals(x.FullName, y.FullName);
        public int GetHashCode([DisallowNull] FileInfo obj) => obj.FullName.GetHashCode();
    }
}
