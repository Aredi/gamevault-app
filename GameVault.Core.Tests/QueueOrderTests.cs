using GameVault.Core.Downloads;

namespace GameVault.Core.Tests
{
    public class QueueOrderTests
    {
        [Fact]
        public void Move_ChangesThePlace_WithinTheList()
        {
            var queue = new List<string> { "a", "b", "c" };
            Assert.True(QueueOrder.Move(queue, "c", -1));
            Assert.Equal(new[] { "a", "c", "b" }, queue);
            Assert.True(QueueOrder.Move(queue, "a", 5));
            Assert.Equal(new[] { "c", "b", "a" }, queue);
            Assert.False(QueueOrder.Move(queue, "c", -1));
            Assert.False(QueueOrder.Move(queue, "missing", 1));
            Assert.Equal(new[] { "c", "b", "a" }, queue);
        }
    }
}
