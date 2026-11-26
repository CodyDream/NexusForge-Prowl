// Unity-style Debug forwarding to Prowl.Runtime.Debug.

using System;

namespace UnityEngine;

/// <summary>Minimal Unity-compatible logging facade over <see cref="Prowl.Runtime.Debug"/>.</summary>
public static class Debug
{
    public static void Log(object? message) => Prowl.Runtime.Debug.Log(message);
    public static void Log(string message) => Prowl.Runtime.Debug.Log(message);

    public static void LogWarning(object? message) => Prowl.Runtime.Debug.LogWarning(message);
    public static void LogWarning(string message) => Prowl.Runtime.Debug.LogWarning(message);

    public static void LogError(object? message) => Prowl.Runtime.Debug.LogError(message);
    public static void LogError(string message) => Prowl.Runtime.Debug.LogError(message);

    public static void LogException(Exception exception) => Prowl.Runtime.Debug.LogException(exception);
}
