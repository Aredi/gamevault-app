using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameVault.Core.Library
{
    public class GameCollection
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("game_ids")]
        public List<int> GameIds { get; set; } = new();

        public override string ToString() => Name;
    }

    /// <summary>
    /// Personal collections ("Favourites", "Co-op", ...) of a user profile, stored as JSON next to its config.
    /// They are local: the GameVault server has bookmarks but no collections.
    /// </summary>
    public class GameCollectionStore
    {
        private readonly string file;
        private readonly object fileLock = new();

        public GameCollectionStore(string file)
        {
            this.file = file;
        }

        public List<GameCollection> Load()
        {
            lock (fileLock)
            {
                try
                {
                    if (!File.Exists(file))
                        return new List<GameCollection>();
                    return JsonSerializer.Deserialize<List<GameCollection>>(File.ReadAllText(file)) ?? new List<GameCollection>();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Could not read the collections in {file}");
                    return new List<GameCollection>();
                }
            }
        }

        private void Save(List<GameCollection> collections)
        {
            lock (fileLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                string temp = file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(collections, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, file, true);
            }
        }

        private static GameCollection? Find(List<GameCollection> collections, string name) =>
            collections.FirstOrDefault(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Creates the collection (names are unique, case-insensitive) and returns it.</summary>
        public GameCollection Create(string name)
        {
            name = name.Trim();
            if (name.Length == 0)
                throw new ArgumentException("The collection name is empty.");
            var collections = Load();
            GameCollection? existing = Find(collections, name);
            if (existing != null)
                return existing;
            var collection = new GameCollection { Name = name };
            collections.Add(collection);
            Save(collections);
            return collection;
        }

        public void Rename(string name, string newName)
        {
            newName = newName.Trim();
            if (newName.Length == 0)
                throw new ArgumentException("The collection name is empty.");
            var collections = Load();
            GameCollection collection = Find(collections, name) ?? throw new ArgumentException($"There is no collection named '{name}'.");
            GameCollection? other = Find(collections, newName);
            if (other != null && other != collection)
                throw new ArgumentException($"A collection named '{newName}' already exists.");
            collection.Name = newName;
            Save(collections);
        }

        public void Delete(string name)
        {
            var collections = Load();
            if (collections.RemoveAll(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) > 0)
                Save(collections);
        }

        public void SetMembership(string name, int gameId, bool member)
        {
            var collections = Load();
            GameCollection collection = Find(collections, name) ?? throw new ArgumentException($"There is no collection named '{name}'.");
            bool changed = member ? !collection.GameIds.Contains(gameId) : collection.GameIds.Contains(gameId);
            if (!changed)
                return;
            if (member)
                collection.GameIds.Add(gameId);
            else
                collection.GameIds.RemoveAll(id => id == gameId);
            Save(collections);
        }

        public List<string> CollectionsOf(int gameId) => Load().Where(c => c.GameIds.Contains(gameId)).Select(c => c.Name).ToList();
    }
}
