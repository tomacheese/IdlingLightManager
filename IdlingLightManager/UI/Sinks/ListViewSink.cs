using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace IdlingLightManager.UI.Sinks;

/// <summary>
/// Custom Serilog sink that displays log events in a ListView.
/// </summary>
internal sealed class ListViewSink : ILogEventSink
{
    /// <summary>Target ListView, or <c>null</c> if not set yet.</summary>
    private ListView? _target;

    /// <summary>
    /// Sets the target ListView. Injected after construction to avoid a circular DI dependency.
    /// </summary>
    /// <param name="listView">The ListView control that displays the log.</param>
    public void SetTarget(ListView listView) => _target = listView;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (!IsUsableTarget(_target)) return;

        try
        {
            // Marshal to the UI thread with BeginInvoke when called from another thread
            if (_target!.InvokeRequired)
                _target.BeginInvoke(() => AddItem(logEvent));
            else
                AddItem(logEvent);
        }
        catch (ObjectDisposedException)
        {
            // The ListView may already be disposed (e.g. during form shutdown)
        }
        catch (InvalidOperationException)
        {
            // Ignore races where the handle is not created yet or already disposed
        }
    }

    /// <summary>
    /// Determines whether the ListView can be updated.
    /// </summary>
    /// <param name="target">The ListView to check.</param>
    /// <returns><c>true</c> if the ListView is usable.</returns>
    private static bool IsUsableTarget(ListView? target) =>
        target is not null && !target.IsDisposed && target.IsHandleCreated;

    /// <summary>
    /// Adds a log entry to the ListView and removes the oldest rows when the row cap is exceeded.
    /// </summary>
    /// <param name="logEvent">The log event to add.</param>
    private void AddItem(LogEvent logEvent)
    {
        if (!IsUsableTarget(_target)) return;

        try
        {
            var item = new ListViewItem(logEvent.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            item.SubItems.Add(logEvent.RenderMessage(CultureInfo.InvariantCulture));
            _target!.Items.Add(item);

            while (_target.Items.Count > 200)
                _target.Items.RemoveAt(0);

            _target.Items[^1].EnsureVisible();
        }
        catch (ObjectDisposedException)
        {
            // The ListView may already be disposed (e.g. during form shutdown)
        }
        catch (InvalidOperationException)
        {
            // Ignore races where the handle is not created yet or already disposed
        }
    }
}
