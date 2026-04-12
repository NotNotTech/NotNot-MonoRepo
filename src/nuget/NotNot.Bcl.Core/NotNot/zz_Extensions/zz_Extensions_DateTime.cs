// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] 
// [!!] Copyright ©️ NotNot Project and Contributors. 
// [!!] This file is licensed to you under the MPL-2.0.
// [!!] See the LICENSE.md file in the project root for more info. 
// [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!] [!!]  [!!] [!!] [!!] [!!]

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Blake3;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Helpers;
// using Newtonsoft.Json.Linq; // Removed - no longer needed
using Nito.AsyncEx.Synchronous;
using NotNot;
using NotNot._internal.Threading;
using NotNot.Collections.Advanced;
using NotNot.Collections.SpanLike;
using NotNot.Data;
using NotNot.Diagnostics;

//using Xunit.Sdk;

//using CommunityToolkit.HighPerformance;
//using DotNext;

/// <summary>
///    Extension methods for the DateTimeOffset data type.
/// </summary>
[SuppressMessage("Microsoft.Design", "CA1050:DeclareTypesInNamespaces")]

public static class zz_Extensions_DateTime
{
	private const int EveningEnds = 2;
	private const int MorningEnds = 12;
	private const int AfternoonEnds = 6;
	private static readonly DateTime Date1970 = new(1970, 1, 1);

	/// <summary>
	///    Return System UTC Offset
	/// </summary>
#pragma warning disable NO1001 // Illegal use of local time
	public static TimeSpan _UtcOffset => DateTime.Now.Subtract(DateTime.UtcNow);
#pragma warning restore NO1001 // Illegal use of local time

	/// <summary>
	///    To Iso String, including timezone offset.
	///    format used for java libraries, omits trailing miliseconds
	///    <para>example output: 2012-01-04T19:20:00+07:00</para>
	/// </summary>
	/// <param name="dateTime"></param>
	/// <returns></returns>
	public static string _ToIso(this DateTime dateTime, bool includeMs = false)
	{
		if (dateTime.Kind == DateTimeKind.Utc)
		{
			//print with "Z" suffix
			if (includeMs)
			{
				return dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
			}

			return dateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
		}
		else
		{
			//print with timezone offsets
			if (includeMs)
			{
				return dateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffzzz", CultureInfo.InvariantCulture);
			}

			return dateTime.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
		}
	}

	///// <summary>
	/////    To Iso string, in UTC format
	///// </summary>
	///// <param name="dateTime"></param>
	///// <returns></returns>
	//public static string _ToIsoUtc(this DateTime dateTime, bool includeMs = false)
	//{

	//   return dateTime.ToUniversalTime()._ToIso(includeMs);

	//   //// Ensure the DateTime is in UTC
	//   //DateTime utcDateTime = dateTime.Kind == DateTimeKind.Unspecified
	//   //   ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
	//   //   : dateTime.ToUniversalTime();

	//   //// Format to ISO 8601
	//   ////return utcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

	//   //if (includeMs)
	//   //{
	//   //   return utcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffK", CultureInfo.InvariantCulture);
	//   //}

	//   //return utcDateTime.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
	//}

	/// <summary>
	///    Returns the number of days in the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The number of days.</returns>
	public static int _GetCountDaysOfMonth(this DateTime date)
	{
		var nextMonth = date.AddMonths(1);
		return new DateTime(nextMonth.Year, nextMonth.Month, 1).AddDays(-1).Day;
	}

	/// <summary>
	///    Returns the first day of the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The first day of the month</returns>
	public static DateTime _GetFirstDayOfMonth(this DateTime date)
	{
		return new DateTime(date.Year, date.Month, 1);
	}

	/// <summary>
	///    Returns the first day of the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="dayOfWeek">The desired day of week.</param>
	/// <returns>The first day of the month</returns>
	public static DateTime _GetFirstDayOfMonth(this DateTime date, DayOfWeek dayOfWeek)
	{
		var dt = date._GetFirstDayOfMonth();
		while (dt.DayOfWeek != dayOfWeek)
		{
			dt = dt.AddDays(1);
		}

		return dt;
	}

	/// <summary>
	///    Returns the last day of the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The last day of the month.</returns>
	public static DateTime _GetLastDayOfMonth(this DateTime date)
	{
		return new DateTime(date.Year, date.Month, date._GetCountDaysOfMonth());
	}

	/// <summary>
	///    Returns the last day of the month of the provided date.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="dayOfWeek">The desired day of week.</param>
	/// <returns>The date time</returns>
	public static DateTime _GetLastDayOfMonth(this DateTime date, DayOfWeek dayOfWeek)
	{
		var dt = date._GetLastDayOfMonth();
		while (dt.DayOfWeek != dayOfWeek)
		{
			dt = dt.AddDays(-1);
		}

		return dt;
	}

	///// <summary>
	/////    Indicates whether the date is today.
	///// </summary>
	///// <param name="dt">The date.</param>
	///// <returns>
	/////    <c>true</c> if the specified date is today; otherwise, <c>false</c>.
	///// </returns>
	//public static bool _IsToday(this DateTime dt)
	//{
	//   return dt.Date == DateTime.Today;
	//}

	/// <summary>
	///    Sets the time on the specified DateTime value.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="hours">The hours to be set.</param>
	/// <param name="minutes">The minutes to be set.</param>
	/// <param name="seconds">The seconds to be set.</param>
	/// <returns>The DateTime including the new time value</returns>
	public static DateTime _SetTime(this DateTime date, int hours, int minutes, int seconds)
	{
		return date._SetTime(new TimeSpan(hours, minutes, seconds));
	}

	/// <summary>
	///    Sets the time on the specified DateTime value.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="time">The TimeSpan to be applied.</param>
	/// <returns>
	///    The DateTime including the new time value
	/// </returns>
	public static DateTime _SetTime(this DateTime date, TimeSpan time)
	{
		return date.Date.Add(time);
	}


	/// <summary>
	///    Gets the first day of the week using the current culture.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The first day of the week</returns>
	public static DateTime _GetFirstDayOfWeek(this DateTime date)
	{
		return date._GetFirstDayOfWeek(null);
	}

	/// <summary>
	///    Gets the first day of the week using the specified culture.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="cultureInfo">The culture to determine the first weekday of a week.</param>
	/// <returns>The first day of the week</returns>
	public static DateTime _GetFirstDayOfWeek(this DateTime date, CultureInfo cultureInfo)
	{
		cultureInfo = cultureInfo ?? CultureInfo.CurrentCulture;

		var firstDayOfWeek = cultureInfo.DateTimeFormat.FirstDayOfWeek;
		while (date.DayOfWeek != firstDayOfWeek)
		{
			date = date.AddDays(-1);
		}

		return date;
	}

	/// <summary>
	///    Gets the last day of the week using the current culture.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The first day of the week</returns>
	public static DateTime _GetLastDayOfWeek(this DateTime date)
	{
		return date._GetLastDayOfWeek(null);
	}

	/// <summary>
	///    Gets the last day of the week using the specified culture.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="cultureInfo">The culture to determine the first weekday of a week.</param>
	/// <returns>The first day of the week</returns>
	public static DateTime _GetLastDayOfWeek(this DateTime date, CultureInfo cultureInfo)
	{
		return date._GetFirstDayOfWeek(cultureInfo).AddDays(6);
	}

	/// <summary>
	///    Gets the next occurence of the specified weekday within the current week using the current culture.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="weekday">The desired weekday.</param>
	/// <returns>The calculated date.</returns>
	/// <example>
	///    <code>
	/// 		var thisWeeksMonday = DateTime.Now.GetWeekday(DayOfWeek.Monday);
	/// 	</code>
	/// </example>
	public static DateTime _GetWeeksWeekday(this DateTime date, DayOfWeek weekday)
	{
		return date._GetWeeksWeekday(weekday, null);
	}

	/// <summary>
	///    Gets the next occurence of the specified weekday within the current week using the specified culture.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="weekday">The desired weekday.</param>
	/// <param name="cultureInfo">The culture to determine the first weekday of a week.</param>
	/// <returns>The calculated date.</returns>
	/// <example>
	///    <code>
	/// 		var thisWeeksMonday = DateTime.Now.GetWeekday(DayOfWeek.Monday);
	/// 	</code>
	/// </example>
	public static DateTime _GetWeeksWeekday(this DateTime date, DayOfWeek weekday, CultureInfo cultureInfo)
	{
		var firstDayOfWeek = date._GetFirstDayOfWeek(cultureInfo);
		return firstDayOfWeek._GetNextWeekday(weekday);
	}

	/// <summary>
	///    Gets the next occurence of the specified weekday.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="weekday">The desired weekday.</param>
	/// <returns>The calculated date.</returns>
	/// <example>
	///    <code>
	/// 		var lastMonday = DateTime.Now.GetNextWeekday(DayOfWeek.Monday);
	/// 	</code>
	/// </example>
	public static DateTime _GetNextWeekday(this DateTime date, DayOfWeek weekday)
	{
		while (date.DayOfWeek != weekday)
		{
			date = date.AddDays(1);
		}

		return date;
	}

	/// <summary>
	///    Gets the previous occurence of the specified weekday.
	/// </summary>
	/// <param name="date">The base date.</param>
	/// <param name="weekday">The desired weekday.</param>
	/// <returns>The calculated date.</returns>
	/// <example>
	///    <code>
	/// 		var lastMonday = DateTime.Now.GetPreviousWeekday(DayOfWeek.Monday);
	/// 	</code>
	/// </example>
	public static DateTime _GetPreviousWeekday(this DateTime date, DayOfWeek weekday)
	{
		while (date.DayOfWeek != weekday)
		{
			date = date.AddDays(-1);
		}

		return date;
	}

	/// <summary>
	///    Determines whether the date only part of twi DateTime values are equal.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <param name="dateToCompare">The date to compare with.</param>
	/// <returns>
	///    <c>true</c> if both date values are equal; otherwise, <c>false</c>.
	/// </returns>
	public static bool _IsDateEqual(this DateTime date, DateTime dateToCompare)
	{
		return date.Date == dateToCompare.Date;
	}

	/// <summary>
	///    Determines whether the time only part of two DateTime values are equal.
	/// </summary>
	/// <param name="time">The time.</param>
	/// <param name="timeToCompare">The time to compare.</param>
	/// <returns>
	///    <c>true</c> if both time values are equal; otherwise, <c>false</c>.
	/// </returns>
	public static bool _IsTimeEqual(this DateTime time, DateTime timeToCompare)
	{
		return time.TimeOfDay == timeToCompare.TimeOfDay;
	}

	/// <summary>
	///    Get milliseconds of UNIX era. This is the milliseconds since 1/1/1970
	/// </summary>
	/// <param name="dateTime">Up to which time.</param>
	/// <returns>number of milliseconds.</returns>
	/// <remarks>
	///    Contributed by blaumeister, http://www.codeplex.com/site/users/view/blaumeiser
	/// </remarks>
	public static long _GetMillisecondsSince1970(this DateTime dateTime)
	{
		var ts = dateTime.Subtract(Date1970);
		return (long)ts.TotalMilliseconds;
	}

	/// <summary>
	///    Indicates whether the specified date is a weekend (Saturday or Sunday).
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>
	///    <c>true</c> if the specified date is a weekend; otherwise, <c>false</c>.
	/// </returns>
	public static bool _IsWeekend(this DateTime date)
	{
		return date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

		// return date.DayOfWeek._EqualsAny(DayOfWeek.Saturday, DayOfWeek.Sunday);
	}

	/// <summary>
	///    Adds the specified amount of weeks (=7 days gregorian calendar) to the passed date value.
	/// </summary>
	/// <param name="date">The origin date.</param>
	/// <param name="value">The amount of weeks to be added.</param>
	/// <returns>The enw date value</returns>
	public static DateTime _AddWeeks(this DateTime date, int value)
	{
		return date.AddDays(value * 7);
	}

	/// <summary>
	///    Get the number of days within that year.
	/// </summary>
	/// <param name="year">The year.</param>
	/// <returns>the number of days within that year</returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static int _GetDays(int year)
	{
		var first = new DateTime(year, 1, 1);
		var last = new DateTime(year + 1, 1, 1);
		return first._GetDays(last);
	}

	/// <summary>
	///    Get the number of days within that date year.
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>the number of days within that year</returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static int _GetDays(this DateTime date)
	{
		return _GetDays(date.Year);
	}

	/// <summary>
	///    Get the number of days between two dates.
	/// </summary>
	/// <param name="fromDate">The origin year.</param>
	/// <param name="toDate">To year</param>
	/// <returns>The number of days between the two years</returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static int _GetDays(this DateTime fromDate, DateTime toDate)
	{
		return Convert.ToInt32(toDate.Subtract(fromDate).TotalDays);
	}

	/// <summary>
	///    Return a period "Morning", "Afternoon", or "Evening"
	/// </summary>
	/// <param name="date">The date.</param>
	/// <returns>The period "morning", "afternoon", or "evening"</returns>
	/// <remarks>
	///    Contributed by Michael T, http://about.me/MichaelTran
	/// </remarks>
	public static string _GetPeriodOfDay(this DateTime date)
	{
		var hour = date.Hour;
		if (hour < EveningEnds)
		{
			return "evening";
		}

		if (hour < MorningEnds)
		{
			return "morning";
		}

		return hour < AfternoonEnds ? "afternoon" : "evening";
	}

	/// <summary>
	///    Gets the week number for a provided date time value based on the current culture settings.
	/// </summary>
	/// <param name="dateTime">The date time.</param>
	/// <returns>The week number</returns>
	public static int _GetWeekOfYear(this DateTime dateTime)
	{
		var culture = CultureInfo.CurrentUICulture;
		var calendar = culture.Calendar;
		var dateTimeFormat = culture.DateTimeFormat;

		return calendar.GetWeekOfYear(dateTime, dateTimeFormat.CalendarWeekRule, dateTimeFormat.FirstDayOfWeek);
	}

	/// <summary>
	///    Indicates whether the specified date is Easter in the Christian calendar.
	/// </summary>
	/// <param name="date">Instance value.</param>
	/// <returns>True if the instance value is a valid Easter Date.</returns>
	public static bool _IsEaster(this DateTime date)
	{
		int Y = date.Year;
		int a = Y % 19;
		int b = Y / 100;
		int c = Y % 100;
		int d = b / 4;
		int e = b % 4;
		int f = (b + 8) / 25;
		int g = (b - f + 1) / 3;
		int h = (19 * a + b - d - g + 15) % 30;
		int i = c / 4;
		int k = c % 4;
		int L = (32 + 2 * e + 2 * i - h - k) % 7;
		int m = (a + 11 * h + 22 * L) / 451;
		int Month = (h + L - 7 * m + 114) / 31;
		int Day = (h + L - 7 * m + 114) % 31 + 1;

		DateTime dtEasterSunday = new(Y, Month, Day);

		return date == dtEasterSunday;
	}

	/// <summary>
	///    Indicates whether the source DateTime is before the supplied DateTime.
	/// </summary>
	/// <param name="source">The source DateTime.</param>
	/// <param name="other">The compared DateTime.</param>
	/// <returns>True if the source is before the other DateTime, False otherwise</returns>
	public static bool _IsBefore(this DateTime source, DateTime other)
	{
		return source.CompareTo(other) < 0;
	}

	/// <summary>
	///    Indicates whether the source DateTime is before the supplied DateTime.
	/// </summary>
	/// <param name="source">The source DateTime.</param>
	/// <param name="other">The compared DateTime.</param>
	/// <returns>True if the source is before the other DateTime, False otherwise</returns>
	public static bool _IsAfter(this DateTime source, DateTime other)
	{
		return source.CompareTo(other) > 0;
	}

	/// <summary>
	///    returns the lower of the two values
	/// </summary>
	/// <returns></returns>
	public static DateTime _Min(this DateTime source, DateTime other)
	{
		return source.CompareTo(other) < 0 ? source : other;
	}

	/// <summary>
	///    returns the higher of the two values
	/// </summary>
	/// <returns></returns>
	public static DateTime _Max(this DateTime source, DateTime other)
	{
		return source.CompareTo(other) > 0 ? source : other;
	}

	public static double _DaysAgo(this DateTime source)
	{
		return (DateTime.UtcNow - source).TotalDays;
	}

	/// <summary>
	/// Formats a DateTime as a compact, significance-based local time string.
	/// Converts to local time, then displays with decreasing precision based on age.
	/// </summary>
	/// <param name="dateTime">The DateTime to format (any Kind — will be converted to local).</param>
	/// <returns>
	/// <list type="bullet">
	/// <item><b>&lt;24h ago</b>: <c>HH.MM.SS</c> (24-hour local time, e.g. <c>14.32.07</c>)</item>
	/// <item><b>&lt;1 month ago</b>: <c>MM-DD:HH.MM</c> (e.g. <c>01-28:14.32</c>)</item>
	/// <item><b>&gt;=1 month ago</b>: <c>YYYY-MM-DD:HH.MM</c> (e.g. <c>2026-01-28:14.32</c>)</item>
	/// </list>
	/// </returns>
	public static string _ToStringSignificantLocal(this DateTime dateTime)
	{
		var local = dateTime.Kind == DateTimeKind.Local ? dateTime : dateTime.ToLocalTime();
		var now = DateTime.UtcNow.ToLocalTime();
		var age = now - local;

		if (age.TotalHours >= 0 && age.TotalHours < 24)
		{
			return local.ToString("HH:mm.ss");
		}

		// Use calendar month comparison: same year+month = "less than 1 month"
		// Guard: monthDiff >= 0 prevents future dates from losing year information
		var monthDiff = (now.Year - local.Year) * 12 + now.Month - local.Month;
		if (monthDiff >= 0 && (monthDiff < 1 || (monthDiff == 1 && now.Day < local.Day)))
		{
			return local.ToString("MM-dd @ HH:mm");
		}

		return local.ToString("yyyy-MM-dd @ HH:mm");
	}

	/// <summary>
	/// Truncates DateTime to microsecond precision (6 decimal places) for PostgreSQL compatibility.
	/// WHY: PostgreSQL timestamp has microsecond precision while .NET DateTime has 100-nanosecond precision.
	/// This prevents roundtrip test failures and ensures consistent timestamps across database operations.
	/// </summary>
	/// <param name="dateTime">The DateTime to truncate</param>
	/// <returns>DateTime truncated to microsecond precision</returns>
	/// <example>
	/// var now = DateTime.UtcNow._ToMicrosecondPrecision();
	/// // 2025-01-01 12:00:00.1234567 becomes 2025-01-01 12:00:00.123456
	/// </example>
	public static DateTime _ToMicrosecondPrecision(this DateTime dateTime)
	{
		// PostgreSQL has microsecond precision (6 decimal places after seconds)
		// 1 tick = 100 nanoseconds
		// 1 microsecond = 1000 nanoseconds = 10 ticks
		// We truncate to 10-tick boundaries to match PostgreSQL precision
		var ticks = dateTime.Ticks;
		var microsecondTicks = (ticks / 10) * 10; // Truncate to 10-tick boundaries
		return new DateTime(microsecondTicks, dateTime.Kind);
	}

	/// <summary>
	/// Truncates nullable DateTime to microsecond precision for PostgreSQL compatibility.
	/// WHY: Provides null-safe version of precision truncation for optional timestamp fields.
	/// </summary>
	/// <param name="dateTime">The nullable DateTime to truncate</param>
	/// <returns>Nullable DateTime truncated to microsecond precision, or null if input is null</returns>
	public static DateTime? _ToMicrosecondPrecision(this DateTime? dateTime)
	{
		return dateTime?._ToMicrosecondPrecision();
	}

	/// <summary>
	/// Truncates DateTimeOffset to microsecond precision for PostgreSQL compatibility.
	/// WHY: PostgreSQL timestamptz also has microsecond precision limitation.
	/// </summary>
	/// <param name="dateTimeOffset">The DateTimeOffset to truncate</param>
	/// <returns>DateTimeOffset truncated to microsecond precision</returns>
	public static DateTimeOffset _ToMicrosecondPrecision(this DateTimeOffset dateTimeOffset)
	{
		var ticks = dateTimeOffset.Ticks;
		var microsecondTicks = (ticks / 10) * 10;
		return new DateTimeOffset(microsecondTicks, dateTimeOffset.Offset);
	}

	/// <summary>
	/// Truncates nullable DateTimeOffset to microsecond precision for PostgreSQL compatibility.
	/// WHY: Provides null-safe version for optional timestamptz fields.
	/// </summary>
	/// <param name="dateTimeOffset">The nullable DateTimeOffset to truncate</param>
	/// <returns>Nullable DateTimeOffset truncated to microsecond precision, or null if input is null</returns>
	public static DateTimeOffset? _ToMicrosecondPrecision(this DateTimeOffset? dateTimeOffset)
	{
		return dateTimeOffset?._ToMicrosecondPrecision();
	}
}

