using System;

namespace MyWeatherSyncMod
{
    // ✅ TimeOfDay 直接定义在命名空间下，不是嵌套在 TimeSyncManager 内
    public enum TimeOfDay
    {
        Morning,
        Day,
        Afternoon,
        Dusk,
        Night,
        LateNight
    }

    public static class TimeSyncManager
    {
        public static TimeOfDay GetTimeOfDay(DateTime sunrise, DateTime sunset)
        {
            var now = DateTime.Now;
            if (now < sunrise.AddHours(-1)) return TimeOfDay.LateNight;
            if (now < sunrise.AddHours(2)) return TimeOfDay.Morning;
            if (now < sunset.AddHours(-3)) return TimeOfDay.Day;
            if (now < sunset.AddMinutes(-30)) return TimeOfDay.Afternoon;
            if (now < sunset.AddHours(1)) return TimeOfDay.Dusk;
            return TimeOfDay.Night;
        }
    }
}