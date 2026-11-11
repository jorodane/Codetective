using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class CrtSurfaceMathTests
    {
        private const double HalfWidth = 560;
        private const double HalfHeight = 352;
        private const double Bulge = 32;

        [Test]
        public void CenterProtrudesTowardViewerWhileCornersStayOnOriginalPlane()
        {
            Assert.That(Height(0, 0), Is.EqualTo(-Bulge));
            Assert.That(Height(HalfWidth, 0), Is.EqualTo(-Bulge / 2));
            Assert.That(Height(0, HalfHeight), Is.EqualTo(-Bulge / 2));
            Assert.That(Height(HalfWidth, HalfHeight), Is.Zero);
            Assert.That(Height(-HalfWidth, -HalfHeight), Is.Zero);
        }

        [Test]
        public void CurvatureIsSymmetricAndZeroBulgeRestoresFlatGeometry()
        {
            Assert.That(Height(230, 120), Is.EqualTo(Height(-230, -120)));
            Assert.That(CrtSurfaceMath.Height(230, 120, HalfWidth, HalfHeight, 0), Is.Zero);
        }

        [Test]
        public void ClippedScrollContentDoesNotBendUnboundedlyBehindMonitor()
        {
            Assert.That(Height(0, 100000), Is.EqualTo(Height(0, HalfHeight)));
            Assert.That(Height(-100000, -100000), Is.Zero);
        }

        [TestCase(0, 0)]
        [TestCase(560, 0)]
        [TestCase(0, 352)]
        [TestCase(-560, -352)]
        [TestCase(560, 352)]
        public void PerpendicularPointerHitsTheVisibleSurface(double x, double y)
        {
            Assert.That(Hit(x, y, -1000, 0, 0, 1, true, out double t), Is.True);
            Assert.That(-1000 + t, Is.EqualTo(Height(x, y)).Within(1e-8));
        }

        [TestCase(0.0001)]
        [TestCase(1)]
        [TestCase(3200)]
        public void ObliquePointersRoundTripAcrossTheScreenWithNonNormalizedDirections(double scale)
        {
            for (int x = -500; x <= 500; x += 100)
            {
                for (int y = -300; y <= 300; y += 100)
                {
                    double dx = 0.3 * scale, dy = -0.2 * scale, dz = scale;
                    double ox = x - dx * 100, oy = y - dy * 100, oz = Height(x, y) - dz * 100;
                    Assert.That(Hit(ox, oy, oz, dx, dy, dz, true, out double t), Is.True);
                    Assert.That(ox + dx * t, Is.EqualTo(x).Within(1e-5));
                    Assert.That(oy + dy * t, Is.EqualTo(y).Within(1e-5));
                    Assert.That(oz + dz * t, Is.EqualTo(Height(x, y)).Within(1e-5));
                }
            }
        }

        [TestCase(561, 0)]
        [TestCase(0, 353)]
        [TestCase(-561, -353)]
        public void PointerOutsideMonitorCannotClickItsControls(double x, double y)
        {
            Assert.That(Hit(x, y, -1000, 0, 0, 1, true, out _), Is.False);
        }

        [Test]
        public void DragCanContinuePastTheScreenEdge()
        {
            Assert.That(Hit(580, 0, -1000, 0, 0, 1, false, out double t), Is.True);
            Assert.That(t, Is.GreaterThan(0));
        }

        [Test]
        public void BackFacingAndBehindTheRayHitsAreRejected()
        {
            Assert.That(Hit(0, 0, 1000, 0, 0, -1, true, out _), Is.False);
            Assert.That(Hit(0, 0, -10, 0, 0, 1, true, out _), Is.False);
        }

        [Test]
        public void FlatSurfaceStillHasAnExactIntersectionAndRejectsParallelRays()
        {
            Assert.That(CrtSurfaceMath.TryIntersect(10, 10, -1000, 0, 0, 1,
                HalfWidth, HalfHeight, 0, true, out double t), Is.True);
            Assert.That(t, Is.EqualTo(1000));
            Assert.That(CrtSurfaceMath.TryIntersect(10, 10, -1000, 1, 0, 0,
                HalfWidth, HalfHeight, 0, true, out _), Is.False);
        }

        [Test]
        public void NearlyPerpendicularRaysDoNotLosePrecision()
        {
            Assert.That(Hit(0, 0, -1000, 1e-9, 0, 1, true, out double t), Is.True);
            Assert.That(t, Is.EqualTo(968).Within(1e-7));
        }

        [Test]
        public void InvalidSurfaceDimensionsAreRejected()
        {
            Assert.That(CrtSurfaceMath.TryIntersect(0, 0, -1000, 0, 0, 1,
                0, HalfHeight, Bulge, true, out _), Is.False);
            Assert.That(CrtSurfaceMath.TryIntersect(0, 0, -1000, 0, 0, 1,
                HalfWidth, 0, Bulge, true, out _), Is.False);
        }

        private static double Height(double x, double y) =>
            CrtSurfaceMath.Height(x, y, HalfWidth, HalfHeight, Bulge);

        private static bool Hit(double ox, double oy, double oz, double dx, double dy, double dz,
            bool requireInside, out double t) =>
            CrtSurfaceMath.TryIntersect(ox, oy, oz, dx, dy, dz,
                HalfWidth, HalfHeight, Bulge, requireInside, out t);
    }
}
