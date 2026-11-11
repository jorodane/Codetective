using System.Collections.Generic;
using System.Reflection;
using Codetective.CameraViews;
using NUnit.Framework;
using UnityEngine;

namespace Codetective.Tests.CameraViews
{
    public sealed class CameraAngleTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (Object obj in created)
                if (obj != null)
                    Object.DestroyImmediate(obj);
            created.Clear();
        }

        [Test]
        public void BlendInterpolatesAllPoseComponentsAndReachesTheExactEndpoint()
        {
            var preset = Preset("monitor", new Vector3(2, 1, -4), new Vector3(0, 90, 0));
            var blend = new CameraAngleTransition(Pose.identity, 60, preset);
            blend.Step(0.5f);
            AssertPose(blend.CurrentPose, new Pose(new Vector3(1, 0.5f, -2), Quaternion.Euler(0, 45, 0)));
            Assert.That(blend.CurrentFieldOfView, Is.EqualTo(38).Within(0.0001f));
            blend.Step(10);
            Assert.That(blend.IsComplete, Is.True);
            AssertPose(blend.CurrentPose, preset.Pose);
            Assert.That(blend.CurrentFieldOfView, Is.EqualTo(16));
        }

        [Test]
        public void RetargetingStartsAtTheIntermediateViewWithoutJumping()
        {
            var first = new CameraAngleTransition(Pose.identity, 60,
                Preset("first", new Vector3(2, 1, -4), new Vector3(0, 90, 0)));
            first.Step(0.4f);
            var next = new CameraAngleTransition(first.CurrentPose, first.CurrentFieldOfView,
                Preset("second", new Vector3(-2, 3, 1), new Vector3(30, 0, 0)));
            next.Step(0);
            AssertPose(next.CurrentPose, first.CurrentPose);
            Assert.That(next.CurrentFieldOfView, Is.EqualTo(first.CurrentFieldOfView));
        }

        [Test]
        public void ZeroDurationIsImmediateAndDoesNotDivideByZero()
        {
            var preset = Preset("instant", Vector3.one, Vector3.zero);
            Set(preset, "transitionDuration", 0f);
            var blend = new CameraAngleTransition(Pose.identity, 60, preset);
            Assert.That(blend.IsComplete, Is.True);
            AssertPose(blend.CurrentPose, preset.Pose);
            Assert.That(blend.CurrentFieldOfView, Is.EqualTo(16));
        }

        [Test]
        public void BlendIsIndependentOfHowTimeIsSplitIntoFrames()
        {
            var preset = Preset("target", Vector3.one, new Vector3(20, 70, 10));
            var smallFrames = new CameraAngleTransition(Pose.identity, 60, preset);
            var largeFrames = new CameraAngleTransition(Pose.identity, 60, preset);
            for (int i = 0; i < 6; i++) smallFrames.Step(0.1f);
            for (int i = 0; i < 3; i++) largeFrames.Step(0.2f);
            AssertPose(smallFrames.CurrentPose, largeFrames.CurrentPose);
            Assert.That(smallFrames.CurrentFieldOfView, Is.EqualTo(largeFrames.CurrentFieldOfView).Within(0.0001f));
        }

        [Test]
        public void RotationTakesTheShortPathAcrossZeroDegrees()
        {
            var preset = Preset("target", Vector3.zero, new Vector3(0, 10, 0));
            var start = new Pose(Vector3.zero, Quaternion.Euler(0, 350, 0));
            var blend = new CameraAngleTransition(start, 60, preset);
            blend.Step(0.5f);
            Assert.That(Quaternion.Angle(blend.CurrentPose.rotation, Quaternion.identity), Is.LessThan(0.05f));
        }

        [Test]
        public void AuthoredCurveEndpointsAreNormalized()
        {
            var preset = Preset("target", new Vector3(10, 0, 0), Vector3.zero);
            Set(preset, "blendCurve", AnimationCurve.Linear(0, 2, 1, 4));
            var blend = new CameraAngleTransition(Pose.identity, 60, preset);
            AssertPose(blend.CurrentPose, Pose.identity);
            blend.Step(0.5f);
            Assert.That(blend.CurrentPose.position.x, Is.EqualTo(5).Within(0.0001f));
            blend.Step(0.5f);
            AssertPose(blend.CurrentPose, preset.Pose);
        }

        [Test]
        public void EditingTheAssetDoesNotChangeAnInFlightTarget()
        {
            var preset = Preset("target", Vector3.one, new Vector3(0, 90, 0));
            Pose original = preset.Pose;
            var blend = new CameraAngleTransition(Pose.identity, 60, preset);
            Set(preset, "position", Vector3.one * 20);
            Set(preset, "fieldOfView", 90f);
            Set(preset, "transitionDuration", 10f);
            preset.BlendCurve.keys = AnimationCurve.Linear(0, 0, 1, 0).keys;
            blend.Step(1);
            Assert.That(blend.IsComplete, Is.True);
            AssertPose(blend.CurrentPose, original);
            Assert.That(blend.CurrentFieldOfView, Is.EqualTo(16));
        }

        [Test]
        public void RegistrySkipsEmptySlotsAndRejectsAmbiguousIds()
        {
            var overview = Preset("overview", Vector3.zero, Vector3.zero);
            var monitor = Preset("monitor", Vector3.zero, Vector3.zero);
            CameraAngleSet set = New<CameraAngleSet>();
            var angles = new List<CameraAnglePreset> { overview, null, monitor };
            Set(set, "angles", angles);
            Set(set, "initialIndex", 1);
            Assert.That(set.InitialIndex, Is.Zero);
            Assert.That(set.NextIndex(0, 1), Is.EqualTo(2));
            Assert.That(set.NextIndex(0, -1), Is.EqualTo(2));
            Assert.That(set.FindId(" MONITOR "), Is.EqualTo(2));
            Assert.That(set.Get(-1), Is.Null);
            Assert.That(set.IndexOf(null), Is.EqualTo(-1));
            angles.Add(Preset("monitor", Vector3.one, Vector3.zero));
            Assert.That(set.FindId("monitor"), Is.EqualTo(-1));
        }

        [Test]
        public void WorldSpacePresetIsIndependentOfCameraParentTransform()
        {
            var parent = NewObject("parent");
            parent.transform.SetPositionAndRotation(new Vector3(8, 3, -4), Quaternion.Euler(0, 90, 0));
            var cameraObject = NewObject("camera");
            cameraObject.transform.SetParent(parent.transform);
            var controller = cameraObject.AddComponent<CameraAngleController>();
            var preset = Preset("monitor", new Vector3(-0.3f, 1.2627725f, -3.6f), new Vector3(7, 0, 0));
            CameraAngleSet set = New<CameraAngleSet>();
            Set(set, "angles", new List<CameraAnglePreset> { preset });
            Set(controller, "angleSet", set);
            Assert.That(controller.TryGoToIndex(0, true), Is.True);
            AssertPose(new Pose(cameraObject.transform.position, cameraObject.transform.rotation), preset.Pose);
            Assert.That(controller.ControlledCamera.fieldOfView, Is.EqualTo(16));
        }

        [Test]
        public void OriginRelativeAnglesCanBeCapturedAndAppliedWithTranslatedScaledOrigin()
        {
            var origin = NewObject("origin");
            origin.transform.SetPositionAndRotation(new Vector3(10, 3, -4), Quaternion.Euler(0, 90, 0));
            origin.transform.localScale = Vector3.one * 2;
            var controller = NewObject("camera").AddComponent<CameraAngleController>();
            var preset = Preset("relative", new Vector3(1, 2, 3), new Vector3(7, 20, 0));
            CameraAngleSet set = New<CameraAngleSet>();
            Set(set, "angles", new List<CameraAnglePreset> { preset });
            Set(controller, "angleSet", set);
            Set(controller, "angleOrigin", origin.transform);
            Assert.That(controller.TryGoToIndex(0, true), Is.True);
            Assert.That(Vector3.Distance(controller.transform.position, origin.transform.TransformPoint(preset.Pose.position)),
                Is.LessThan(0.0001f));
            AssertPose(controller.CapturePose(), preset.Pose);
            Set(controller, "angleOrigin", controller.transform);
            Assert.That(controller.TryGoToIndex(0, true), Is.False);
        }

        private CameraAnglePreset Preset(string id, Vector3 position, Vector3 euler)
        {
            var preset = New<CameraAnglePreset>();
            Set(preset, "angleId", id);
            Set(preset, "position", position);
            Set(preset, "eulerAngles", euler);
            Set(preset, "fieldOfView", 16f);
            Set(preset, "transitionDuration", 1f);
            Set(preset, "blendCurve", AnimationCurve.Linear(0, 0, 1, 1));
            return preset;
        }

        private T New<T>() where T : ScriptableObject
        {
            T obj = ScriptableObject.CreateInstance<T>();
            created.Add(obj);
            return obj;
        }

        private GameObject NewObject(string name)
        {
            var obj = new GameObject(name);
            created.Add(obj);
            return obj;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static void AssertPose(Pose actual, Pose expected)
        {
            Assert.That(Vector3.Distance(actual.position, expected.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(actual.rotation, expected.rotation), Is.LessThan(0.05f));
        }
    }
}
