using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PasswordManagerLocal.Frontend.Services;

/// <summary>
/// Defines the frontend/backend time boundary.
/// Backend values are interpreted as UTC and converted to the device's current local time only for display.
/// Frontend values are converted to UTC before they are passed to backend request models.
/// </summary>
public static class FrontendDateTimeUtil
{
    public static DateTime ToLocalFromBackendUtc(DateTime value)
    {
        if (value == default)
            return default;

        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return utc.ToLocalTime();
    }

    public static DateTimeOffset ToLocalFromBackendUtc(DateTimeOffset value) =>
        value == default ? default : value.ToUniversalTime().ToLocalTime();

    public static DateTimeOffset? ToLocalFromBackendUtc(DateTimeOffset? value) =>
        value is { } dateTimeOffset ? ToLocalFromBackendUtc(dateTimeOffset) : null;

    public static DateTime ToBackendUtc(DateTime value)
    {
        if (value == default)
            return default;

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
        };
    }

    public static DateTime? ToBackendUtc(DateTime? value) =>
        value is { } dateTime ? ToBackendUtc(dateTime) : null;

    public static DateTimeOffset ToBackendUtc(DateTimeOffset value) =>
        value == default ? default : value.ToUniversalTime();

    public static DateTimeOffset? ToBackendUtc(DateTimeOffset? value) =>
        value is { } dateTimeOffset ? ToBackendUtc(dateTimeOffset) : null;

    public static T NormalizeRequestToUtc<T>(T request)
    {
        if (request is null)
            return request;

        NormalizeObjectGraph(request, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return request;
    }

    private static void NormalizeObjectGraph(object value, HashSet<object> visited)
    {
        var type = value.GetType();
        if (IsSimpleType(type) || !visited.Add(value))
            return;

        if (value is IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
            {
                if (item is not null)
                    NormalizeObjectGraph(item, visited);
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                continue;

            object? propertyValue;
            try
            {
                propertyValue = property.GetValue(value);
            }
            catch
            {
                continue;
            }

            if (property.PropertyType == typeof(DateTime) && property.CanWrite && propertyValue is DateTime dateTime)
            {
                property.SetValue(value, ToBackendUtc(dateTime));
                continue;
            }

            if (property.PropertyType == typeof(DateTime?) && property.CanWrite)
            {
                property.SetValue(value, propertyValue is DateTime nullableDateTime ? ToBackendUtc(nullableDateTime) : null);
                continue;
            }

            if (property.PropertyType == typeof(DateTimeOffset) && property.CanWrite && propertyValue is DateTimeOffset dateTimeOffset)
            {
                property.SetValue(value, ToBackendUtc(dateTimeOffset));
                continue;
            }

            if (property.PropertyType == typeof(DateTimeOffset?) && property.CanWrite)
            {
                property.SetValue(value, propertyValue is DateTimeOffset nullableDateTimeOffset ? ToBackendUtc(nullableDateTimeOffset) : null);
                continue;
            }

            if (propertyValue is not null && !IsSimpleType(property.PropertyType))
                NormalizeObjectGraph(propertyValue, visited);
        }
    }

    private static bool IsSimpleType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsPrimitive ||
               underlyingType.IsEnum ||
               underlyingType == typeof(string) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(Guid) ||
               underlyingType == typeof(DateTime) ||
               underlyingType == typeof(DateTimeOffset) ||
               underlyingType == typeof(TimeSpan);
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
