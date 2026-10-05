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
            CleanupKind.InstalledGameArchive => "archive of an installed game",
            CleanupKind.NotInstalledDownload => "downloaded, not installed",
            CleanupKind.OrphanPrefix => "Wine prefix of an uninstalled game",
            _ => "Proton/Wine version no game uses",
        };

        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set { isSelected = value; OnPropertyChanged(); selectionChanged(); }
        }
    }
}
