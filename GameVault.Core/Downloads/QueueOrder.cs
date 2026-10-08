namespace GameVault.Core.Downloads
{
    public static class QueueOrder
    {
        /// <summary>Moves <paramref name="item"/> by <paramref name="offset"/> places (negative: earlier), within the list. False when it does not move.</summary>
        public static bool Move<T>(IList<T> list, T item, int offset)
        {
            int index = list.IndexOf(item);
            if (index < 0)
                return false;
            int target = Math.Clamp(index + offset, 0, list.Count - 1);
            if (target == index)
                return false;
            list.RemoveAt(index);
            list.Insert(target, item);
            return true;
        }
    }
}
