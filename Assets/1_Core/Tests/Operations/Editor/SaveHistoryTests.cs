using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using Codetective.Operations;
using NUnit.Framework;

namespace Codetective.Tests.Operations
{
    public sealed class SaveHistoryTests
    {
        private string directory;
        private DateTimeOffset now;
        private SaveHistory history;
        // Exercise the real file store without requiring Unity's native JsonUtility runtime.
        private static string Encode(GameSaveRecord record)
        {
            using (var stream = new MemoryStream())
            { new DataContractJsonSerializer(typeof(GameSaveRecord)).WriteObject(stream, record); return Encoding.UTF8.GetString(stream.ToArray()); }
        }
        private static GameSaveRecord Decode(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (GameSaveRecord)new DataContractJsonSerializer(typeof(GameSaveRecord)).ReadObject(stream);
        }
        [SetUp] public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "codetective-saves-" + Guid.NewGuid().ToString("N"));
            now = new DateTimeOffset(2026, 11, 18, 1, 2, 3, TimeSpan.Zero);
            history = new SaveHistory(directory, Encode, Decode, () => now);
        }
        [TearDown] public void Cleanup() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        private GameSaveSlot Save(int n, GameSaveKind kind = GameSaveKind.Manual) => history.Save("snapshot-" + n, kind, n, 540 + n, "상담 " + n);
        [Test]
        public void MoreThanTenMixedSavesKeepOnlyTheTenNewestFiles()
        {
            string oldest = null;
            for (int n = 1; n <= 17; n++)
            {
                GameSaveSlot slot = Save(n, (GameSaveKind)(n % 3)); if (n == 1) oldest = slot.Id;
                Assert.That(history.Slots.Count, Is.LessThanOrEqualTo(10));
            }
            Assert.That(history.Slots.Select(s => s.Record.day), Is.EqualTo(Enumerable.Range(8, 10).Reverse()));
            Assert.That(Directory.GetFiles(directory, "*.json").Length, Is.EqualTo(10));
            Assert.That(Directory.GetFiles(directory, "*.info").Length, Is.EqualTo(10));
            Assert.That(() => history.Read(oldest), Throws.TypeOf<CommandException>());
        }
        [Test]
        public void SelectingAnOlderSaveLoadsThatExactRecordAfterAStoreRestart()
        {
            GameSaveSlot older = Save(1); Save(2); Save(3);
            var reopened = new SaveHistory(directory, Encode, Decode);
            Assert.That(reopened.Read(older.Id).snapshot, Is.EqualTo("snapshot-1"));
            Assert.That(reopened.Slots.First().Record.snapshot, Is.Null);
            reopened.Slots.First().Record.situation = "changed";
            Assert.That(history.Slots.First().Record.situation, Is.EqualTo("상담 3"));
        }
        [Test]
        public void SaveTypesGameSituationAndRealUtcTimeRoundTrip()
        {
            now = new DateTimeOffset(2026, 11, 18, 10, 2, 3, TimeSpan.FromHours(9));
            string snapshot = "{\"단서\":\"상담 내용\\n둘째 줄\"}";
            GameSaveSlot saved = history.Save(snapshot, GameSaveKind.Quick, 4, 987.5, "비밀 시설 조사 · 상담 중");
            GameSaveRecord loaded = history.Read(saved.Id);
            Assert.That(loaded.kind, Is.EqualTo(GameSaveKind.Quick)); Assert.That(loaded.day, Is.EqualTo(4));
            Assert.That(loaded.minute, Is.EqualTo(987.5)); Assert.That(loaded.situation, Is.EqualTo("비밀 시설 조사 · 상담 중"));
            Assert.That(loaded.snapshot, Is.EqualTo(snapshot));
            Assert.That(DateTimeOffset.ParseExact(loaded.savedAtUtc, "O", CultureInfo.InvariantCulture).UtcDateTime, Is.EqualTo(now.UtcDateTime));
        }
        [Test]
        public void FileOrderingRemainsStableWhenWallClockMovesBackwardsOrTimestampsTie()
        {
            Save(1); Save(2); now = now.AddYears(-1); Save(3);
            Assert.That(history.Slots.Select(s => s.Record.day), Is.EqualTo(new[] { 3, 2, 1 }));
        }
        [Test]
        public void DuplicateAutomaticCallbacksDoNotPushOutAnIdenticalManualOrQuickSave()
        {
            Save(1); history.Save("snapshot-1", GameSaveKind.Auto, 1, 541, "자동 콜백");
            Assert.That(history.Slots.Count, Is.EqualTo(1)); Assert.That(history.Slots[0].Record.kind, Is.EqualTo(GameSaveKind.Manual));
            Save(1, GameSaveKind.Quick); history.Save("snapshot-1", GameSaveKind.Auto, 1, 541, "자동 콜백");
            Assert.That(history.Slots.Count, Is.EqualTo(2)); Assert.That(history.Slots[0].Record.kind, Is.EqualTo(GameSaveKind.Quick));
            Save(2, GameSaveKind.Auto); Assert.That(history.Slots.Count, Is.EqualTo(3));
        }
        [Test]
        public void CorruptNewestFileDoesNotPreventSelectingAnEarlierSaveOrCreatingAnother()
        {
            GameSaveSlot earlier = Save(1), corrupt = Save(2);
            File.WriteAllText(Directory.GetFiles(directory, "save-*.json").Single(p => p.Contains(corrupt.Id)), "broken JSON");
            Assert.That(history.Slots[0].Available, Is.False); Assert.That(history.Read(earlier.Id).day, Is.EqualTo(1));
            Assert.That(() => history.Read(corrupt.Id), Throws.TypeOf<CommandException>());
            Save(3); Assert.That(history.Slots[0].Record.day, Is.EqualTo(3));
        }
        [Test]
        public void ListingTenLargeSavesReadsOnlySmallMetadataAndLoadingReadsOnlyTheSelectedPayload()
        {
            for (int n = 1; n <= 10; n++) history.Save(new string('x', 256 * 1024) + n, GameSaveKind.Manual, n, 540, "상담 중");
            int payloads = 0, characters = 0;
            var reopened = new SaveHistory(directory, Encode, json =>
            { characters += json.Length; GameSaveRecord record = Decode(json); if (record.snapshot != null) payloads++; return record; });
            var slots = reopened.Slots;
            Assert.That(slots.Count, Is.EqualTo(10)); Assert.That(slots.All(s => s.Record.snapshot == null), Is.True);
            Assert.That(payloads, Is.Zero); Assert.That(characters, Is.LessThan(10000));
            Assert.That(reopened.Read(slots[7].Id).day, Is.EqualTo(3)); Assert.That(payloads, Is.EqualTo(1));
        }
        [Test]
        public void OldSaveFilesWithoutIndexesAreIndexedOnceWithoutRewritingTheirPayloads()
        {
            Save(1); Save(2);
            string[] payloads = Directory.GetFiles(directory, "save-*.json").OrderBy(p => p).Select(File.ReadAllText).ToArray();
            foreach (string info in Directory.GetFiles(directory, "*.info")) File.Delete(info);
            int reads = 0;
            var reopened = new SaveHistory(directory, Encode, json => { var record = Decode(json); if (record.snapshot != null) reads++; return record; });
            Assert.That(reopened.Slots.Count, Is.EqualTo(2)); Assert.That(reads, Is.EqualTo(2));
            Assert.That(reopened.Slots.Count, Is.EqualTo(2)); Assert.That(reads, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(directory, "save-*.json").OrderBy(p => p).Select(File.ReadAllText), Is.EqualTo(payloads));
        }
        [Test]
        public void ACorruptIndexFallsBackToTheIntactSaveAndRepairsIt()
        {
            GameSaveSlot saved = Save(4);
            File.WriteAllText(Directory.GetFiles(directory, "*.info").Single(), "broken index");
            Assert.That(history.Slots.Single().Record.day, Is.EqualTo(4));
            Assert.That(File.ReadAllText(Directory.GetFiles(directory, "*.info").Single()), Does.StartWith("Codetective Save Info 1"));
            Assert.That(history.Read(saved.Id).snapshot, Is.EqualTo("snapshot-4"));
        }
        [Test]
        public void SerializationFailureCannotPruneExistingSavesOrLeaveATemporaryFile()
        {
            for (int n = 1; n <= 10; n++) Save(n);
            string[] ids = history.Slots.Select(s => s.Id).ToArray();
            var failing = new SaveHistory(directory, _ => throw new IOException("write failed"), Decode);
            Assert.That(() => failing.Save("new", GameSaveKind.Manual, 1, 600, "상담"), Throws.TypeOf<IOException>());
            Assert.That(history.Slots.Select(s => s.Id), Is.EqualTo(ids));
            Assert.That(Directory.GetFiles(directory, "*.tmp"), Is.Empty);
        }
        [Test]
        public void RetentionIgnoresUnrelatedFilesAndIncompleteWrites()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "save-not-owned.json"), "keep");
            File.WriteAllText(Path.Combine(directory, "save-pending.json.tmp"), "pending");
            for (int n = 1; n <= 12; n++) Save(n);
            Assert.That(history.Slots.Count, Is.EqualTo(10));
            Assert.That(File.ReadAllText(Path.Combine(directory, "save-not-owned.json")), Is.EqualTo("keep"));
            Assert.That(File.Exists(Path.Combine(directory, "save-pending.json.tmp")), Is.True);
            Assert.That(() => history.Read("../save-not-owned.json"), Throws.TypeOf<CommandException>());
        }
        [Test]
        public void LegacyAutosaveImportsOnceWithItsOriginalTimeAndLeavesTheOriginalIntact()
        {
            Directory.CreateDirectory(directory); string legacy = Path.Combine(directory, "daily-autosave.json");
            File.WriteAllText(legacy, "old snapshot"); DateTime written = now.AddDays(-5).UtcDateTime; File.SetLastWriteTimeUtc(legacy, written);
            int descriptions = 0;
            LegacySaveDescription Describe(string json)
            { descriptions++; Assert.That(json, Is.EqualTo("old snapshot")); return new LegacySaveDescription { Day = 3, Minute = 600, Situation = "이전 상담" }; }
            Assert.That(history.ImportLegacy(legacy, Describe), Is.True); Assert.That(history.ImportLegacy(legacy, Describe), Is.False);
            GameSaveRecord record = history.Slots.Single().Record;
            Assert.That(record.kind, Is.EqualTo(GameSaveKind.Auto)); Assert.That(record.day, Is.EqualTo(3));
            Assert.That(DateTimeOffset.ParseExact(record.savedAtUtc, "O", CultureInfo.InvariantCulture).UtcDateTime, Is.EqualTo(written).Within(TimeSpan.FromSeconds(2)));
            Assert.That(File.ReadAllText(legacy), Is.EqualTo("old snapshot")); Assert.That(descriptions, Is.EqualTo(1));
            foreach (string file in Directory.GetFiles(directory, "save-*.json")) File.Delete(file);
            Assert.That(history.ImportLegacy(legacy, Describe), Is.False);
        }
        [Test]
        public void InvalidLegacyDataCannotOverwriteOrDeleteAnything()
        {
            Directory.CreateDirectory(directory); string legacy = Path.Combine(directory, "daily-autosave.json"); File.WriteAllText(legacy, "invalid");
            Assert.That(() => history.ImportLegacy(legacy, _ => throw new CommandException("invalid")), Throws.TypeOf<CommandException>());
            Assert.That(history.Slots, Is.Empty); Assert.That(File.ReadAllText(legacy), Is.EqualTo("invalid"));
            Assert.That(File.Exists(Path.Combine(directory, "legacy-imported")), Is.False);
        }
        [TestCase(0, 540)] [TestCase(1, -1)] [TestCase(1, 1441)]
        public void InvalidGameTimeCannotCreateASave(int day, double minute)
        {
            Assert.That(() => history.Save("data", GameSaveKind.Manual, day, minute, ""), Throws.TypeOf<CommandException>());
            Assert.That(history.Slots, Is.Empty);
        }
    }
}
