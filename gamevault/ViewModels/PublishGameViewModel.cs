using GameVault.Core.Publishing;
using gamevault.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace gamevault.ViewModels
{
    internal class PublishGameViewModel : ViewModelBase
    {
        private static readonly string[] ArchiveExtensions = { ".zip", ".7z", ".rar", ".iso", ".gz", ".xz", ".bz2", ".tar" };

        private string sourcePath = "";
        /// <summary>A game folder, an installer (its folder is published) or an archive / ISO published as it is.</summary>
        public string SourcePath
        {
            get => sourcePath;
            set { sourcePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsArchiveSource)); OnPropertyChanged(nameof(HasSource)); RefreshFileName(); }
        }
        public bool HasSource => !string.IsNullOrEmpty(sourcePath);
        public bool IsArchiveSource => File.Exists(sourcePath) && Array.Exists(ArchiveExtensions, e => sourcePath.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        /// <summary>The folder that is zipped (the source folder, or the folder of the chosen installer).</summary>
        public string SourceFolder => Directory.Exists(sourcePath) ? sourcePath : Path.GetDirectoryName(sourcePath) ?? "";

        private string title = "";
        public string Title
        {
            get => title;
            set { title = value; OnPropertyChanged(); RefreshFileName(); }
        }
        private string version = "";
        public string Version
        {
            get => version;
            set { version = value; OnPropertyChanged(); RefreshFileName(); }
        }
        private string year = "";
        public string Year
        {
            get => year;
            set { year = value; OnPropertyChanged(); RefreshFileName(); }
        }
        private bool earlyAccess;
        public bool EarlyAccess
        {
            get => earlyAccess;
            set { earlyAccess = value; OnPropertyChanged(); RefreshFileName(); }
        }
        private int typeIndex;
        /// <summary>0 = Windows portable, 1 = Windows installer, 2 = Linux portable.</summary>
        public int TypeIndex
        {
            get => typeIndex;
            set { typeIndex = Math.Max(0, value); OnPropertyChanged(); OnPropertyChanged(nameof(IsSetup)); OnPropertyChanged(nameof(IsPortable)); RefreshFileName(); }
        }
        public PublishedGameType Type => (PublishedGameType)typeIndex;
        public bool IsSetup => Type == PublishedGameType.WindowsSetup;
        public bool IsPortable => !IsSetup;

        private List<string> executables = new();
        public List<string> Executables
        {
            get => executables;
            set { executables = value; OnPropertyChanged(); }
        }
        private string? selectedExecutable;
        public string? SelectedExecutable
        {
            get => selectedExecutable;
            set { selectedExecutable = value; OnPropertyChanged(); }
        }
        private List<string> installers = new();
        public List<string> Installers
        {
            get => installers;
            set { installers = value; OnPropertyChanged(); }
        }
        private string? selectedInstaller;
        public string? SelectedInstaller
        {
            get => selectedInstaller;
            set { selectedInstaller = value; OnPropertyChanged(); }
        }
        private string installerParameters = "";
        public string InstallerParameters
        {
            get => installerParameters;
            set { installerParameters = value; OnPropertyChanged(); }
        }
        private string installerHint = "";
        public string InstallerHint
        {
            get => installerHint;
            set { installerHint = value; OnPropertyChanged(); }
        }
        private string launchParameters = "";
        public string LaunchParameters
        {
            get => launchParameters;
            set { launchParameters = value; OnPropertyChanged(); }
        }

        private string targetDirectory = "";
        /// <summary>The folder the server reads its games from (mounted as /files in the server container).</summary>
        public string TargetDirectory
        {
            get => targetDirectory;
            set { targetDirectory = value; OnPropertyChanged(); }
        }
        private int destinationIndex;
        /// <summary>0 = copy into a folder this computer can open, 1 = upload to the GameVault Uploader.</summary>
        public int DestinationIndex
        {
            get => destinationIndex;
            set { destinationIndex = Math.Clamp(value, 0, 1); OnPropertyChanged(); OnPropertyChanged(nameof(IsUpload)); }
        }
        public bool IsUpload => destinationIndex == 1;
        private string uploaderUrl = "";
        public string UploaderUrl
        {
            get => uploaderUrl;
            set { uploaderUrl = value; OnPropertyChanged(); }
        }
        private string uploaderStatus = "";
        public string UploaderStatus
        {
            get => uploaderStatus;
            set { uploaderStatus = value; OnPropertyChanged(); }
        }
        private bool compress;
        public bool Compress
        {
            get => compress;
            set { compress = value; OnPropertyChanged(); }
        }

        private string fileName = "";
        public string FileName
        {
            get => fileName;
            private set { fileName = value; OnPropertyChanged(); }
        }
        public void RefreshFileName()
        {
            try
            {
                string extension = IsArchiveSource ? ArchiveExtension(sourcePath) : ".zip";
                FileName = string.IsNullOrWhiteSpace(title) ? "" : GameFileName.Build(title, version, Type, int.TryParse(year, out int y) ? y : null, earlyAccess, extension);
            }
            catch { FileName = ""; }
        }

        /// <summary>".tar.gz" counts as one extension.</summary>
        public static string ArchiveExtension(string file)
        {
            Match match = Regex.Match(file, @"(\.tar\.(gz|xz|bz2)|\.[A-Za-z0-9]+)$");
            return match.Success ? match.Value.ToLowerInvariant() : ".zip";
        }

        private bool isBusy;
        public bool IsBusy
        {
            get => isBusy;
            set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
        }
        public bool IsIdle => !isBusy;
        private double progress;
        public double Progress
        {
            get => progress;
            set { progress = value; OnPropertyChanged(); }
        }
        private string status = "";
        public string Status
        {
            get => status;
            set { status = value; OnPropertyChanged(); }
        }
        private Game? publishedGame;
        public Game? PublishedGame
        {
            get => publishedGame;
            set { publishedGame = value; OnPropertyChanged(); }
        }
    }
}
