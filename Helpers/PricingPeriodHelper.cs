using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ClassIsland.Shared;

namespace DeepSeekBalance.Helpers;

/// <summary>
/// DeepSeek 峰谷计价时段判定。
/// 规则（北京时间）：
///   高峰时段 = 周一至周五（不含中国法定节假日）9:00-12:00、14:00-18:00；
///   其余时段（含周末与法定节假日全天）为空闲时段。
/// 法定节假日数据来自 AdvancedTimeIsland 插件（本插件的必需依赖）：
///   优先调用其对外公开接口 AdvancedTimeIsland.Shared.HolidayApi.GetHolidayDates(int)；
///   若安装的是尚未提供该接口的旧版 ATI，则回退为反射读取
///   AttendanceCalendarService.Instance.Data.Holidays。
/// 两个插件运行在各自的 AssemblyLoadContext 中，故统一按类型名反射调用，避免类型标识冲突。
/// </summary>
public static class PricingPeriodHelper
{
    /// <summary>公开接口的类型名（ATI 侧契约）。</summary>
    private const string HolidayApiTypeName = "AdvancedTimeIsland.Shared.HolidayApi";

    /// <summary>旧版 ATI 的日历服务类型名（回退路径）。</summary>
    private const string CalendarServiceTypeName = "AdvancedTimeIsland.Services.AttendanceCalendarService";

    /// <summary>北京时间相对 UTC 的固定偏移（中国无夏令时）。</summary>
    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

    private static readonly TimeSpan MorningPeakStart = new(9, 0, 0);
    private static readonly TimeSpan MorningPeakEnd = new(12, 0, 0);
    private static readonly TimeSpan AfternoonPeakStart = new(14, 0, 0);
    private static readonly TimeSpan AfternoonPeakEnd = new(18, 0, 0);

    /// <summary>节假日数据缓存有效期：容忍 ATI 在本插件运行期间联网更新数据。</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static readonly object CacheLock = new();
    private static int _cachedYear;
    private static DateTime _cachedAtUtc;
    private static HashSet<DateTime>? _cachedHolidays;
    private static bool _hasCache;

    /// <summary>当前北京时间。</summary>
    public static DateTime GetBeijingNow()
        => DateTimeOffset.UtcNow.ToOffset(BeijingOffset).DateTime;

    /// <summary>获取指定北京时间所处时段的展示标签："峰" 或 "谷"。</summary>
    public static string GetLabel(DateTime beijingTime)
        => IsPeak(beijingTime) ? "峰" : "谷";

    /// <summary>判断指定北京时间是否处于高峰时段。</summary>
    public static bool IsPeak(DateTime beijingTime)
    {
        // 周末全天空闲（调休补班日按需求同样归为空闲）。
        if (beijingTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        // 法定节假日全天空闲。
        if (IsStatutoryHoliday(beijingTime.Date))
        {
            return false;
        }

        var time = beijingTime.TimeOfDay;
        return (time >= MorningPeakStart && time < MorningPeakEnd)
               || (time >= AfternoonPeakStart && time < AfternoonPeakEnd);
    }

    /// <summary>
    /// 判断指定日期是否为法定节假日（放假日）。按年份缓存，并设有效期以便跟随 ATI 的数据更新。
    /// 依赖不可用时不缓存、按"非节假日"处理（仍保留周末规则）。
    /// </summary>
    private static bool IsStatutoryHoliday(DateTime date)
    {
        var year = date.Year;

        lock (CacheLock)
        {
            var cacheValid = _hasCache
                             && _cachedYear == year
                             && DateTime.UtcNow - _cachedAtUtc < CacheTtl;

            if (!cacheValid)
            {
                if (!TryGetHolidayDates(year, out var holidays))
                {
                    // 依赖尚未加载 / 接口暂不可用：不做缓存，下次刷新重试。
                    return false;
                }

                _cachedYear = year;
                _cachedHolidays = holidays;
                _cachedAtUtc = DateTime.UtcNow;
                _hasCache = true;
            }

            return _cachedHolidays!.Contains(date.Date);
        }
    }

    /// <summary>
    /// 读取指定年份的节假日日期集合：优先走 ATI 公开接口，失败时回退旧版反射路径。
    /// </summary>
    private static bool TryGetHolidayDates(int year, out HashSet<DateTime> holidays)
        => TryGetHolidayDatesFromApi(year, out holidays)
           || TryGetHolidayDatesFromLegacyService(out holidays);

    /// <summary>
    /// 调用 ATI 公开接口 <c>HolidayApi.GetHolidayDates(int)</c>，返回日期数组。
    /// </summary>
    private static bool TryGetHolidayDatesFromApi(int year, out HashSet<DateTime> holidays)
    {
        holidays = new HashSet<DateTime>();

        try
        {
            var apiType = FindLoadedType(HolidayApiTypeName);
            if (apiType is null)
            {
                return false;
            }

            var method = apiType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetHolidayDates"
                                     && m.GetParameters().Length == 1
                                     && m.GetParameters()[0].ParameterType == typeof(int));
            if (method is null)
            {
                return false;
            }

            if (method.Invoke(null, new object[] { year }) is not IEnumerable dates)
            {
                return false;
            }

            foreach (var item in dates)
            {
                if (item is DateTime holidayDate)
                {
                    holidays.Add(holidayDate.Date);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 旧版 ATI 回退路径：反射读取 CalendarService.Data.Holidays（HolidayDay.Date / IsOffDay）。
    /// </summary>
    private static bool TryGetHolidayDatesFromLegacyService(out HashSet<DateTime> holidays)
    {
        holidays = new HashSet<DateTime>();

        try
        {
            var serviceType = FindLoadedType(CalendarServiceTypeName);
            if (serviceType is null)
            {
                return false;
            }

            // 优先从宿主 DI 解析单例（解析动作会触发其构造），失败再回退静态 Instance。
            var service = TryResolveFromHost(serviceType)
                          ?? serviceType
                              .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
                              ?.GetValue(null);
            if (service is null)
            {
                return false;
            }

            var data = service.GetType().GetProperty("Data")?.GetValue(service);
            if (data?.GetType().GetProperty("Holidays")?.GetValue(data) is not IEnumerable entries)
            {
                return false;
            }

            foreach (var entry in entries)
            {
                if (entry is null)
                {
                    continue;
                }

                var entryType = entry.GetType();
                var isOffDay = entryType.GetProperty("IsOffDay")?.GetValue(entry) as bool? ?? true;
                if (!isOffDay)
                {
                    continue;
                }

                if (entryType.GetProperty("Date")?.GetValue(entry) is DateTime holidayDate)
                {
                    holidays.Add(holidayDate.Date);
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从宿主 DI 解析服务实例。单独成方法，使 ClassIsland.Shared 相关的
    /// 类型加载失败也能被调用方的 try/catch 捕获，不影响降级路径。
    /// </summary>
    private static object? TryResolveFromHost(Type serviceType)
    {
        try
        {
            return IAppHost.Host?.Services.GetService(serviceType);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>在当前进程已加载的程序集中按完整类型名查找类型。</summary>
    private static Type? FindLoadedType(string typeName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var type = assembly.GetType(typeName);
                if (type is not null)
                {
                    return type;
                }
            }
            catch
            {
                // 个别程序集类型加载失败不影响其余查找。
            }
        }

        return null;
    }
}
