using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using CommandLine;
using gamevault.Models;

namespace gamevault
{
    /// <summary>
    /// Main entry point
    /// </summary>
    public class Program
    {
        // Unique ID for the mutex, which will not be shared by another application
        private const string GAMEVAULT_MUTEX = "0C8E52D8-ECD4-4F12-95B5-CE3412C073EA:GameVault";

        [STAThread]
        public static void Main(string[] args)
        {
            CommandOptions cmdLineOptions;
            if (args.Length > 0 && args.Any(s => s.Contains("gamevault://")) && !args[0].Contains("--uridata"))
            {
                List<string> stringList = new List<string>(args);
                stringList.Insert(0, "--uridata");
                args = stringList.ToArray();
            }

            using (var parser = new Parser(with =>
            {
                with.AutoVersion = true;
                with.AutoHelp = true;
                with.HelpWriter = null;

                with.CaseInsensitiveEnumValues = true;
                with.CaseSensitive = false;
                with.IgnoreUnknownArguments = true;
            }))
            {
                var cmdLineParserResult = parser.ParseArguments(args, CommandOptions.CommandLineTypes);

                #region Command Line Help

                cmdLineParserResult.WithNotParsed(errors =>
                {
                    // We only treat uri requests differently for the sake of help, otherwise it's parsed automatically
                    var isFromUri = args.Any(x => x.StartsWith("--uridata", StringComparison.OrdinalIgnoreCase));

                    var help = CommandLine.Text.HelpText.AutoBuild(cmdLineParserResult, help =>
                    {
                        help.AddEnumValuesToHelpText = true;
                        help.AddNewLineBetweenHelpSections = true;
                        help.AdditionalNewLineAfterOption = false;

                        // Attempt to get the actual verb used
                        var action = errors.OfType<HelpVerbRequestedError>().FirstOrDefault()?.Verb ?? "<action>";

                        help.AddPreOptionsLine(Environment.NewLine);

                        if (isFromUri)
                        {
                            help.AddDashesToOption = false;
                            help.AddPreOptionsLine($"USAGE: {PipeServiceHandler.GAMEVAULT_URI_SCHEME}://{action}?[param=value]&...");
                        }
                        else
                        {
                            var executable = Path.GetFileName(Environment.ProcessPath ?? "gamevault");
                            help.AddPreOptionsLine($"USAGE: {executable} {action} [param=value] ...");
                        }

                        return CommandLine.Text.HelpText.DefaultParsingErrorsHandler(cmdLineParserResult, help);
                    }, maxDisplayWidth: 600);

                    Console.WriteLine(help.ToString());

                    if (errors.Any(e => e.StopsProcessing))
                        Environment.Exit(1);
                });

                #endregion Command Line Help

                // Downgrade to CommandOptions which can be treated generically
                if (cmdLineParserResult?.Value is CommandOptions commandOptions)
                    cmdLineOptions = commandOptions;
                else
                    cmdLineOptions = new CommandOptions();
            }

            bool createdMutex = false;
            Mutex? mutex = null;

            // Mutexes are preferred when checking if a program is already running, since in theory another GameVault could be running that isn't ours
            if (!Mutex.TryOpenExisting(GAMEVAULT_MUTEX, out _))
            {
                mutex = new Mutex(true, GAMEVAULT_MUTEX, out createdMutex);
            }

            if (!createdMutex)
            {
                // GameVault is already running and we should send messages to that instance
                if (cmdLineOptions.Action == CommandOptions.ActionEnum.Query)
                {
                    // We're sending a query through the pipe, this is mostly for debugging
                    var result = PipeServiceHandler.SendMessage(cmdLineOptions.UriData!, expectsResult: true).GetAwaiter().GetResult();
                    result = result?.Trim('\r', '\n');
                    if (!string.IsNullOrEmpty(result))
                        Console.WriteLine(result);

                    Environment.Exit(1);
                    return;
                }

                PipeServiceHandler.SendMessage(cmdLineOptions.UriData, expectsResult: false).GetAwaiter().GetResult();
                Environment.Exit(1);
                return;
            }

            PipeServiceHandler.StartInstance();
            App.CommandLineOptions = cmdLineOptions;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
            GC.KeepAlive(mutex);
        }

        // Avalonia configuration, also used by the visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
