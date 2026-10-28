using gamevault.Helper;
using System;
using Xunit;

namespace GameVault.UiTests
{
    public class AndroidScenarios
    {
        /// <summary>A folder chosen in Android's picker comes as a document tree: its path on the phone or the memory card.</summary>
        [Fact]
        public void AFolderOfAndroidsPicker_IsItsPath()
        {
            Assert.Equal("/storage/emulated/0/Games/Retro", StorageHelper.AndroidFolderPath(new Uri("content://com.android.externalstorage.documents/tree/primary%3AGames%2FRetro")));
            Assert.Equal("/storage/emulated/0", StorageHelper.AndroidFolderPath(new Uri("content://com.android.externalstorage.documents/tree/primary%3A")));
            Assert.Equal("/storage/1A2B-3C4D/Roms", StorageHelper.AndroidFolderPath(new Uri("content://com.android.externalstorage.documents/tree/1A2B-3C4D%3ARoms")));
            // Another app's documents, or a way out of the storage: no path
            Assert.Null(StorageHelper.AndroidFolderPath(new Uri("content://com.google.android.apps.docs.storage/tree/abc")));
            Assert.Null(StorageHelper.AndroidFolderPath(new Uri("content://com.android.externalstorage.documents/tree/primary%3A..%2F..%2Fdata")));
        }
    }
}
