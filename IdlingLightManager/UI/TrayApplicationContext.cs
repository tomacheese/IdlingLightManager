using Microsoft.Extensions.Hosting;

namespace IdlingLightManager.UI;

/// <summary>
/// タスクトレイ常駐アプリケーションのコンテキスト。
/// 起動時にフォームを非表示で初期化し、アプリケーション停止時にフォームを閉じる。
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", Justification = "Instantiated by dependency injection.")]
internal sealed class TrayApplicationContext : ApplicationContext
{
    /// <summary>メインフォーム。</summary>
    private readonly MainForm _form;

    /// <summary>ホストのアプリケーションライフタイム。</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>
    /// <see cref="TrayApplicationContext"/> の新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="form">メインフォーム。</param>
    /// <param name="lifetime">ホストのアプリケーションライフタイム。</param>
    public TrayApplicationContext(MainForm form, IHostApplicationLifetime lifetime)
    {
        _form = form;
        _lifetime = lifetime;

        // フォームが閉じられたらメッセージループを終了する
        _form.FormClosed += (_, _) => ExitThread();

        // ホストの停止要求を受けてフォームを閉じる
        _lifetime.ApplicationStopping.Register(() =>
        {
            try
            {
                if (!_form.IsDisposed && _form.IsHandleCreated)
                    _form.BeginInvoke(() => _form.Close());
            }
            catch (InvalidOperationException)
            {
                // ハンドル未作成/破棄済みへのアクセス競合時は無視する
            }
        });

        // 非表示で起動する（NotifyIcon などの初期化を発火させるため Show→Hide の順で呼ぶ）
        _form.WindowState = FormWindowState.Minimized;
        _form.Show();
        _form.Hide();
    }
}
