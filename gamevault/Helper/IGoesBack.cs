namespace gamevault.Helper
{
    /// <summary>A page with a step of its own to go back from (Android's back button): false when it has none.</summary>
    internal interface IGoesBack
    {
        bool GoBack();
    }
}
