using Codetective.Interaction;
using NUnit.Framework;

namespace Codetective.Tests.Interaction
{
    public sealed class InspectionFlowTests
    {
        [Test]
        public void StartsInOverview()
        {
            Assert.That(new InspectionFlow().Mode, Is.EqualTo(InspectionMode.Overview));
        }

        [Test]
        public void EscapeWhileInspectingOpensTheMenuImmediately()
        {
            var flow = new InspectionFlow();
            Assert.That(flow.Inspect(), Is.True);
            Assert.That(flow.Escape(), Is.EqualTo(EscapeAction.OpenMenu));
            Assert.That(flow.Mode, Is.EqualTo(InspectionMode.Menu));
        }

        [Test]
        public void EscapeClosesTheMenuAndRestoresTheInspectedView()
        {
            var flow = new InspectionFlow();
            flow.Inspect();
            Assert.That(flow.Escape(), Is.EqualTo(EscapeAction.OpenMenu));
            Assert.That(flow.Mode, Is.EqualTo(InspectionMode.Menu));
            Assert.That(flow.Escape(), Is.EqualTo(EscapeAction.CloseMenu));
            Assert.That(flow.Mode, Is.EqualTo(InspectionMode.Inspecting));
        }

        [Test]
        public void EscapeInOverviewOpensTheMenuImmediately()
        {
            var flow = new InspectionFlow();
            Assert.That(flow.Escape(), Is.EqualTo(EscapeAction.OpenMenu));
            Assert.That(flow.Mode, Is.EqualTo(InspectionMode.Menu));
        }

        [Test]
        public void MenuBlocksObjectClicksAndOverviewShortcuts()
        {
            var flow = new InspectionFlow();
            flow.Escape();
            Assert.That(flow.Inspect(), Is.False);
            flow.ReturnToOverview();
            Assert.That(flow.Mode, Is.EqualTo(InspectionMode.Menu));
        }

        [Test]
        public void ResumePreservesTheViewThatOpenedTheMenu()
        {
            var flow = new InspectionFlow();
            flow.Inspect();
            flow.Escape();
            Assert.That(flow.Resume(), Is.True);
            Assert.That(flow.Mode, Is.EqualTo(InspectionMode.Inspecting));
            Assert.That(flow.Resume(), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResumeOutsideTheMenuDoesNotChangeTheView(bool inspecting)
        {
            var flow = new InspectionFlow();
            if (inspecting) flow.Inspect();
            InspectionMode before = flow.Mode;
            Assert.That(flow.Resume(), Is.False);
            Assert.That(flow.Mode, Is.EqualTo(before));
        }

        [Test]
        public void SwitchingTargetsDoesNotAccumulateExtraBackSteps()
        {
            var flow = new InspectionFlow();
            flow.Inspect();
            flow.Inspect();
            flow.Inspect();
            flow.ReturnToOverview();
            Assert.That(flow.Mode, Is.EqualTo(InspectionMode.Overview));
        }

        [Test]
        public void OverviewShortcutDoesNotOpenTheMenu()
        {
            var flow = new InspectionFlow();
            flow.Inspect();
            flow.ReturnToOverview();
            Assert.That(flow.Mode, Is.EqualTo(InspectionMode.Overview));
        }
    }
}
