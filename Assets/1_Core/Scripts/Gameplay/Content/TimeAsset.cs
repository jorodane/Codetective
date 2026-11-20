using Codetective.Operations;
using UnityEngine;

namespace Codetective.Gameplay
{
    [CreateAssetMenu(menuName = "Codetective/Data/Time", fileName = "Time")]
    public sealed class TimeAsset : WorldDataAsset
    {
        public override string BuiltinSchemaId => "Time";
        public int year = 1994, month = 1, day = 1, hour = 9, minute;
        public GameDateTime Timestamp => new GameDateTime(year, month, day, hour, minute);
        protected override void Write(DataAssetBuilder b)
        {
            _ = Timestamp.Value;
            b.Set("year", Datum.Number(year)); b.Set("month", Datum.Number(month)); b.Set("day", Datum.Number(day)); b.Set("hour", Datum.Number(hour)); b.Set("minute", Datum.Number(minute));
        }
    }
}
