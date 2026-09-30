using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Telemetry;

namespace WaveOptics;

internal static class WaveOpticsTelemetry
{
    private const int MaxChainLength = 5;
    private const int MaxFrames = 100;

    private static readonly Assembly Own = typeof(WaveOpticsTelemetry).Assembly;
    private static int _started;

    public static void EnsureStartedOnce()
    {
        var application = Application.Current;
        if (application is null || Interlocked.Exchange(ref _started, 1) != 0)
            return;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        application.DispatcherUnhandledException += OnDispatcherUnhandledException;

        TelemetryReporter.Start();
    }

    public static void Report(Exception exception)
    {
        if (Volatile.Read(ref _started) != 0)
            TelemetryReporter.Report(exception);
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => ReportOwn(e.ExceptionObject as Exception);

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        => ReportOwn(e.Exception);

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        => ReportOwn(e.Exception);

    private static void ReportOwn(Exception? exception)
    {
        if (exception is not null && IsOwn(exception))
            TelemetryReporter.Report(exception);
    }

    private static bool IsOwn(Exception exception)
    {
        var current = exception;
        for (var depth = 0; current is not null && depth < MaxChainLength; depth++)
        {
            if (HasOwnFrame(current))
                return true;

            current = current is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
                ? aggregate.InnerExceptions[0]
                : current.InnerException;
        }

        return false;
    }

    private static bool HasOwnFrame(Exception exception)
    {
        StackFrame[]? frames;

        try
        {
            frames = new StackTrace(exception, false).GetFrames();
        }
        catch (Exception)
        {
            return false;
        }

        if (frames is null)
            return false;

        for (var index = 0; index < Math.Min(frames.Length, MaxFrames); index++)
        {
            MethodBase? method;

            try
            {
                method = frames[index].GetMethod();
            }
            catch (Exception)
            {
                continue;
            }

            if (method?.DeclaringType is { } declaringType && ReferenceEquals(declaringType.Assembly, Own))
                return true;
        }

        return false;
    }
}
