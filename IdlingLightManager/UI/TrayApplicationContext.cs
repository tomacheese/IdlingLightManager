using Microsoft.Extensions.Hosting;

namespace IdlingLightManager.UI;

/// <summary>
/// Application context for the tray-resident application.
/// Initializes the form hidden at startup and closes it when the application stops.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by dependency injection.")]
internal sealed class TrayApplicationContext : ApplicationContext
{
    /// <summary>Main form.</summary>
    private readonly MainForm _form;

    /// <summary>Host application lifetime.</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>
    /// Initializes a new instance of the <see cref="TrayApplicationContext"/> class.
    /// </summary>
    /// <param name="form">The main form.</param>
    /// <param name="lifetime">The host application lifetime.</param>
    public TrayApplicationContext(MainForm form, IHostApplicationLifetime lifetime)
    {
        _form = form;
        _lifetime = lifetime;

        _form.FormClosed += (_, _) => ExitThread();

        _lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                if (!_form.IsDisposed && _form.IsHandleCreated)
                    _form.BeginInvoke(() => _form.Close());
            }
            catch (InvalidOperationException)
            {
                // Ignore races where the handle is not created yet or already disposed
            }
        });

        // Start hidden (Show then Hide so that NotifyIcon and other initialization runs)
        _form.WindowState = FormWindowState.Minimized;
        _form.Show();
        _form.Hide();
    }
}
