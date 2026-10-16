// netstandard2.0 doesn't include IsExternalInit which is required for C# 9+ records.
// This polyfill allows the generator project to use record types.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
