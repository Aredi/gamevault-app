using gamevault.Localization;
using GameVault.Core.Storage;
using System;

namespace gamevault.ViewModels
{
    internal class StorageCleanupItem : ViewModelBase
    {
        public StorageCleanupItem(CleanupCandidate candidate, Action selectionChanged)
        {
            Candidate = candidate;
            isSelected = candidate.SelectedByDefault;
            this.selectionChanged = selectionChanged;
        }

        private readonly Action selectionChanged;
        public CleanupCandidate Candidate { get; }
        public string SizeText => StorageCleanup.FormatSize(Candidate.Size);
        public string KindText => Candidate.Kind switch
        {
            CleanupKind.InstalledGameArchive => Loc.T("archive of an installed game"),
            CleanupKind.NotInstalledDownload => Loc.T("downloaded, not installed"),
            CleanupKind.OrphanPrefix => Loc.T("Wine prefix of an uninstalled game"),
            _ => Loc.T("Proton/Wine version no game uses"),
        };

        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set { isSelected = value; OnPropertyChanged(); selectionChanged(); }
        }
    }
}
