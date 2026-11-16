using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Codetective.Terminal.Tests
{
    public sealed class PageNavigationTests
    {
        private static PageNavigation Navigation(int capacity = 64)
        {
            var navigation = new PageNavigation(capacity);
            foreach (string id in new[] { "clinic", "kernel", "clinic-records", "custom" }) navigation.Register(id);
            return navigation;
        }
        [Test]
        public void BackAndForwardRestoreTheSamePagesWithoutDuplicatingHistory()
        {
            PageNavigation navigation = Navigation();
            var visits = new List<string>(); navigation.Changed += visits.Add;
            Assert.That(navigation.Current, Is.Null);
            Assert.That(navigation.Navigate("clinic"), Is.True);
            navigation.Navigate("kernel"); navigation.Navigate("clinic-records");
            Assert.That(navigation.Back(), Is.True); Assert.That(navigation.Current, Is.EqualTo("kernel"));
            Assert.That(navigation.Back(), Is.True); Assert.That(navigation.Current, Is.EqualTo("clinic"));
            Assert.That(navigation.Back(), Is.False);
            Assert.That(navigation.Forward(), Is.True); Assert.That(navigation.Current, Is.EqualTo("kernel"));
            navigation.Forward(); Assert.That(navigation.Forward(), Is.False);
            Assert.That(visits, Is.EqualTo(new[] { "clinic", "kernel", "clinic-records", "kernel", "clinic", "kernel", "clinic-records" }));
        }
        [Test]
        public void ANewDestinationAfterGoingBackDiscardsTheOldForwardBranch()
        {
            PageNavigation navigation = Navigation();
            navigation.Navigate("clinic"); navigation.Navigate("kernel"); navigation.Navigate("clinic-records"); navigation.Back();
            navigation.Navigate("custom");
            Assert.That(navigation.CanGoForward, Is.False);
            navigation.Back(); Assert.That(navigation.Current, Is.EqualTo("kernel"));
            navigation.Forward(); Assert.That(navigation.Current, Is.EqualTo("custom"));
        }
        [Test]
        public void UnknownAndCurrentPagesDoNotModifyHistoryOrEmitChanges()
        {
            PageNavigation navigation = Navigation(); int changes = 0; navigation.Changed += _ => changes++;
            Assert.That(navigation.Navigate(null), Is.False);
            navigation.Navigate("clinic"); navigation.Navigate("kernel"); navigation.Back();
            Assert.That(navigation.Navigate("clinic"), Is.False);
            Assert.That(navigation.Navigate("missing"), Is.False);
            Assert.That(navigation.CanGoForward, Is.True);
            Assert.That(changes, Is.EqualTo(3));
            navigation.Forward(); Assert.That(navigation.Current, Is.EqualTo("kernel"));
        }
        [Test]
        public void HistoryIsBoundedWhileKeepingTheMostRecentDestinations()
        {
            PageNavigation navigation = Navigation(3);
            navigation.Navigate("clinic"); navigation.Navigate("kernel"); navigation.Navigate("clinic-records"); navigation.Navigate("custom");
            navigation.Back(); navigation.Back();
            Assert.That(navigation.Current, Is.EqualTo("kernel"));
            Assert.That(navigation.CanGoBack, Is.False);
            navigation.Forward(); navigation.Forward(); Assert.That(navigation.Current, Is.EqualTo("custom"));
        }
        [Test]
        public void NewPagesCanBeRegisteredWithoutResettingTheCurrentSession()
        {
            PageNavigation navigation = Navigation(); navigation.Navigate("clinic"); navigation.Navigate("kernel");
            navigation.Register("future-page");
            Assert.That(navigation.Current, Is.EqualTo("kernel"));
            Assert.That(navigation.Navigate("future-page"), Is.True);
            navigation.Back(); Assert.That(navigation.Current, Is.EqualTo("kernel"));
        }
        [Test]
        public void InvalidAndDuplicateRegistrationCannotReplaceAnExistingPage()
        {
            PageNavigation navigation = Navigation(); navigation.Navigate("clinic");
            Assert.That(() => navigation.Register("clinic"), Throws.ArgumentException);
            Assert.That(() => navigation.Register(" "), Throws.ArgumentException);
            Assert.That(() => new PageNavigation(1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(navigation.Current, Is.EqualTo("clinic"));
            Assert.That(navigation.CanGoBack, Is.False);
        }

        [Test]
        public void TabCyclingFollowsRegistrationOrderAndWrapsBothWays()
        {
            PageNavigation navigation = Navigation(); navigation.Navigate("clinic");
            navigation.CycleTab(1); Assert.That(navigation.Current, Is.EqualTo("kernel"));
            navigation.CycleTab(1); Assert.That(navigation.Current, Is.EqualTo("clinic-records"));
            navigation.Navigate("custom"); navigation.CycleTab(1); Assert.That(navigation.Current, Is.EqualTo("clinic"));
            navigation.CycleTab(-1); Assert.That(navigation.Current, Is.EqualTo("custom"));
        }
        [Test]
        public void HiddenPagesRemainReachableWithoutAppearingInTheTabCycle()
        {
            var navigation = new PageNavigation();
            navigation.Register("first"); navigation.Register("detail", false); navigation.Register("last");
            navigation.Navigate("first"); navigation.CycleTab(1); Assert.That(navigation.Current, Is.EqualTo("last"));
            navigation.Navigate("detail"); navigation.CycleTab(1); Assert.That(navigation.Current, Is.EqualTo("first"));
            navigation.Navigate("detail"); navigation.CycleTab(-1); Assert.That(navigation.Current, Is.EqualTo("last"));
        }
        [Test]
        public void EmptySingleAndUnselectedTabListsDoNotCreateDuplicateHistory()
        {
            var navigation = new PageNavigation(); int changes = 0; navigation.Changed += _ => changes++;
            Assert.That(navigation.CycleTab(1), Is.False);
            navigation.Register("only"); Assert.That(navigation.CycleTab(-1), Is.True);
            Assert.That(navigation.Current, Is.EqualTo("only"));
            Assert.That(navigation.CycleTab(1), Is.False); Assert.That(navigation.CycleTab(0), Is.False);
            Assert.That(changes, Is.EqualTo(1)); Assert.That(navigation.CanGoBack, Is.False);
        }
        [Test]
        public void CyclingRetainsPageHistoryAndIncludesNewVisibleTabs()
        {
            PageNavigation navigation = Navigation(); navigation.Navigate("clinic"); navigation.CycleTab(1);
            navigation.Back(); Assert.That(navigation.Current, Is.EqualTo("clinic"));
            navigation.Forward(); Assert.That(navigation.Current, Is.EqualTo("kernel"));
            navigation.Register("new-tab"); navigation.Navigate("custom"); navigation.CycleTab(1);
            Assert.That(navigation.Current, Is.EqualTo("new-tab"));
        }
    }
}
