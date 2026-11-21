using NUnit.Framework;

namespace FestivalDerby.Driving.Tests
{
    /// <summary>
    /// RESP1: how far a checkpoint respawn may walk forward off a ramp. The walk must stop one pose short
    /// of the pose nearest the next gate, because that pose can already be past the gate's line.
    /// </summary>
    [TestFixture]
    public sealed class RespawnRouteRulesTests
    {
        private const int Cap = 8;

        [Test]
        public void ForwardBudget_NextGateFarAhead_IsTheCap()
        {
            Assert.That(RespawnRouteRules.ForwardBudget(10, 40, 100, Cap), Is.EqualTo(Cap));
        }

        [Test]
        public void ForwardBudget_NextGateClose_StopsOnePoseShortOfIt()
        {
            Assert.That(RespawnRouteRules.ForwardBudget(10, 13, 100, Cap), Is.EqualTo(2));
        }

        [Test]
        public void ForwardBudget_NextGateAcrossTheRouteWrap_CountsForwardRoundTheLoop()
        {
            // The finish sits near the end of the route and gate zero just after the wrap.
            Assert.That(RespawnRouteRules.ForwardBudget(97, 2, 100, Cap), Is.EqualTo(4));
        }

        [TestCase(10, 10)]
        [TestCase(10, 11)]
        public void ForwardBudget_NextGateOnOrBesideTheStart_NeverWalks(int start, int next)
        {
            Assert.That(RespawnRouteRules.ForwardBudget(start, next, 100, Cap), Is.EqualTo(0));
        }

        [Test]
        public void ForwardBudget_NoGateAhead_IsTheCap()
        {
            Assert.That(RespawnRouteRules.ForwardBudget(10, -1, 100, Cap), Is.EqualTo(Cap));
        }

        [TestCase(-1, 5, 100)]
        [TestCase(5, 9, 0)]
        public void ForwardBudget_NoRouteOrNoStart_IsZero(int start, int next, int routeCount)
        {
            Assert.That(RespawnRouteRules.ForwardBudget(start, next, routeCount, Cap), Is.EqualTo(0));
        }
    }
}
