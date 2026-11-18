using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Codetective.Operations
{
    public enum GameSaveKind { Auto, Manual, Quick }
    [Serializable] public sealed class GameSaveRecord
    {
        public int version = 1;
        public string id, savedAtUtc, situation, snapshot;
        public long order;
        public GameSaveKind kind;
        public int day;
        public double minute;
    }
    public sealed class GameSaveSlot
    {
        public string Id { get; internal set; }
        public GameSaveRecord Record { get; internal set; }
        public string Error { get; internal set; }
        public bool Available => Record != null && Error == null;
    }
    public sealed class LegacySaveDescription
    {
        public int Day;
        public double Minute;
        public string Situation;
    }

    /// <summary>Ten immutable saves, newest first. Publish a complete new file before removing the oldest.</summary>
    public sealed class SaveHistory
    {
        public const int Capacity = 10;
        private static readonly Regex FilePattern = new Regex(@"^save-([0-9]{20})-([0-9a-f]{32})\.json$", RegexOptions.CultureInvariant);
        private readonly string directory;
        private readonly Func<GameSaveRecord, string> encode;
        private readonly Func<string, GameSaveRecord> decode;
        private readonly Func<DateTimeOffset> clock;
        public SaveHistory(string path, Func<GameSaveRecord, string> serialize, Func<string, GameSaveRecord> deserialize, Func<DateTimeOffset> utcNow = null)
        {
            directory = path ?? throw new ArgumentNullException(nameof(path));
            encode = serialize ?? throw new ArgumentNullException(nameof(serialize)); decode = deserialize ?? throw new ArgumentNullException(nameof(deserialize));
            clock = utcNow ?? (() => DateTimeOffset.UtcNow);
        }
        private sealed class SaveFile { public string Path, Id; public long Order; }
        private SaveFile[] Files()
        {
            if (!Directory.Exists(directory)) return Array.Empty<SaveFile>();
            var files = new List<SaveFile>();
            foreach (string path in Directory.GetFiles(directory, "save-*.json", SearchOption.TopDirectoryOnly))
            {
                Match match = FilePattern.Match(Path.GetFileName(path));
                if (match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long order) && order > 0)
                    files.Add(new SaveFile { Path = path, Id = match.Groups[2].Value, Order = order });
            }
            return files.OrderByDescending(f => f.Order).ThenByDescending(f => f.Id, StringComparer.Ordinal).ToArray();
        }
        public IReadOnlyList<GameSaveSlot> Slots => Files().Take(Capacity).Select(ReadFile).ToArray();
        private GameSaveSlot ReadFile(SaveFile file)
        {
            try
            {
                GameSaveRecord record = decode(File.ReadAllText(file.Path)); Validate(record, file.Id, file.Order);
                return new GameSaveSlot { Id = file.Id, Record = record };
            }
            catch (Exception error) { return new GameSaveSlot { Id = file.Id, Error = "저장 기록을 읽을 수 없습니다: " + error.Message }; }
        }
        public GameSaveRecord Read(string id)
        {
            if (!Guid.TryParseExact(id, "N", out _)) throw new CommandException("선택한 저장 기록이 유효하지 않습니다.");
            SaveFile file = Files().Take(Capacity).FirstOrDefault(f => f.Id == id);
            if (file == null) throw new CommandException("선택한 저장 기록이 없습니다.");
            GameSaveSlot slot = ReadFile(file);
            if (!slot.Available) throw new CommandException(slot.Error);
            return slot.Record;
        }
        public GameSaveSlot Save(string snapshot, GameSaveKind kind, int day, double minute, string situation, DateTimeOffset? savedAt = null)
        {
            SaveFile[] files = Files();
            string id = Guid.NewGuid().ToString("N"); long order = checked((files.FirstOrDefault()?.Order ?? 0) + 1);
            var record = new GameSaveRecord { id = id, order = order, savedAtUtc = (savedAt ?? clock()).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                kind = kind, day = day, minute = minute, situation = situation ?? "", snapshot = snapshot };
            Validate(record, id, order);
            // Several callbacks can request the same automatic checkpoint in a single frame.
            if (kind == GameSaveKind.Auto && files.Length > 0)
            {
                GameSaveSlot latest = ReadFile(files[0]);
                if (latest.Available && latest.Record.snapshot == snapshot) return latest;
            }
            string serialized = encode(record); Validate(decode(serialized), id, order);
            string path = Path.Combine(directory, "save-" + order.ToString("D20", CultureInfo.InvariantCulture) + "-" + id + ".json");
            string temporary = path + ".tmp";
            Directory.CreateDirectory(directory);
            try { File.WriteAllText(temporary, serialized); File.Move(temporary, path); }
            catch { if (File.Exists(temporary)) File.Delete(temporary); throw; }
            foreach (SaveFile oldest in Files().Skip(Capacity)) File.Delete(oldest.Path);
            return new GameSaveSlot { Id = id, Record = record };
        }
        private static void Validate(GameSaveRecord record, string id, long order)
        {
            if (record == null || record.version != 1 || record.id != id || record.order != order || order < 1 ||
                !Enum.IsDefined(typeof(GameSaveKind), record.kind) || record.day < 1 || double.IsNaN(record.minute) || double.IsInfinity(record.minute) || record.minute < 0 || record.minute > 1440 ||
                string.IsNullOrWhiteSpace(record.snapshot) || !DateTimeOffset.TryParseExact(record.savedAtUtc, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new CommandException("저장 파일의 정보가 유효하지 않습니다.");
        }
        public bool ImportLegacy(string path, Func<string, LegacySaveDescription> describe)
        {
            string marker = Path.Combine(directory, "legacy-imported");
            if (File.Exists(marker) || Files().Length > 0 || !File.Exists(path)) return false;
            string snapshot = File.ReadAllText(path); LegacySaveDescription info = describe(snapshot);
            Save(snapshot, GameSaveKind.Auto, info.Day, info.Minute, info.Situation, new DateTimeOffset(File.GetLastWriteTimeUtc(path)));
            File.WriteAllText(marker, "1"); // Retain the original file as a compatibility backup.
            return true;
        }
    }
}
