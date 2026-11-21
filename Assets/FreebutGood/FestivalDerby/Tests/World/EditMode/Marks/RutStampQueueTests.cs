using NUnit.Framework;

namespace FestivalDerby.World.Tests
{
    /// <summary>
    /// Contracts for the fixed stamp queue: it never allocates while collecting, it keeps insertion order
    /// (the driver turns the queue straight into matrices), and a full queue drops the excess and says so
    /// once instead of growing or logging per stamp.
    /// </summary>
    [TestFixture]
    public sealed class RutStampQueueTests
    {
        private static RutStamp Stamp(float x)
        {
            return new RutStamp(x, 0f, 0.45f, 1.2f, 0.25f, 0.55f);
        }

        [Test]
        public void Enqueue_KeepsInsertionOrderAndValues()
        {
            var queue = new RutStampQueue();

            Assert.That(queue.TryEnqueue(Stamp(1f)), Is.True);
            Assert.That(queue.TryEnqueue(Stamp(2f)), Is.True);
            Assert.That(queue.Count, Is.EqualTo(2));

            Assert.That(queue[0].X, Is.EqualTo(1f));
            Assert.That(queue[1].X, Is.EqualTo(2f));
            Assert.That(queue[1].Width, Is.EqualTo(0.45f));
            Assert.That(queue[1].Length, Is.EqualTo(1.2f));
            Assert.That(queue[1].YawRadians, Is.EqualTo(0.25f));
            Assert.That(queue[1].Depth, Is.EqualTo(0.55f));
        }

        [Test]
        public void Capacity_IsTheDocumentedCap()
        {
            Assert.That(RutStampQueue.Capacity, Is.EqualTo(512));
        }

        [Test]
        public void Full_QueueDropsTheExcessAndCountsIt()
        {
            var queue = new RutStampQueue();
            for (int i = 0; i < RutStampQueue.Capacity; i++)
                Assert.That(queue.TryEnqueue(Stamp(i)), Is.True);

            Assert.That(queue.Count, Is.EqualTo(RutStampQueue.Capacity));
            Assert.That(queue.TryEnqueue(Stamp(999f)), Is.False);
            Assert.That(queue.TryEnqueue(Stamp(1000f)), Is.False);

            // The refused stamps are not written, and the accepted ones are untouched.
            Assert.That(queue.Count, Is.EqualTo(RutStampQueue.Capacity));
            Assert.That(queue[RutStampQueue.Capacity - 1].X, Is.EqualTo(RutStampQueue.Capacity - 1));
            Assert.That(queue.Dropped, Is.EqualTo(2));
        }

        [Test]
        public void TakeDroppedSinceReport_ReadsOnceThenClears()
        {
            var queue = new RutStampQueue();
            for (int i = 0; i < RutStampQueue.Capacity + 3; i++)
                queue.TryEnqueue(Stamp(i));

            Assert.That(queue.TakeDroppedSinceReport(), Is.EqualTo(3));
            Assert.That(queue.TakeDroppedSinceReport(), Is.EqualTo(0));
            Assert.That(queue.Dropped, Is.EqualTo(3));
        }

        [Test]
        public void Clear_KeepsTheTally_ResetDropsIt()
        {
            var queue = new RutStampQueue();
            queue.TryEnqueue(Stamp(1f));
            for (int i = 0; i < RutStampQueue.Capacity + 1; i++)
                queue.TryEnqueue(Stamp(i));

            queue.Clear();
            Assert.That(queue.Count, Is.EqualTo(0));
            Assert.That(queue.Dropped, Is.EqualTo(2));

            // After a clear the queue is usable again.
            Assert.That(queue.TryEnqueue(Stamp(5f)), Is.True);

            queue.Reset();
            Assert.That(queue.Count, Is.EqualTo(0));
            Assert.That(queue.Dropped, Is.EqualTo(0));
            Assert.That(queue.TakeDroppedSinceReport(), Is.EqualTo(0));
        }
    }
}
