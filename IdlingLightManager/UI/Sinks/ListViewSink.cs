using System.Globalization;
using Serilog.Core;
using Serilog.Events;

namespace IdlingLightManager.UI.Sinks;

/// <summary>
/// Serilog のログイベントを ListView に表示するカスタムシンク。
/// </summary>
internal sealed class ListViewSink : ILogEventSink
{
    /// <summary>ログ出力先の ListView。未設定の場合は <c>null</c>。</summary>
    private ListView? _target;

    /// <summary>
    /// ログ出力先の ListView を設定する（DI 循環依存を回避するため後から注入する）。
    /// </summary>
    /// <param name="listView">ログを表示する ListView コントロール。</param>
    public void SetTarget(ListView listView) => _target = listView;

    /// <inheritdoc />
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        if (!IsUsableTarget(_target)) return;

        try
        {
            // UI スレッド以外から呼ばれた場合は BeginInvoke でマーシャリングする
            if (_target!.InvokeRequired)
                _target.BeginInvoke(() => AddItem(logEvent));
            else
                AddItem(logEvent);
        }
        catch (ObjectDisposedException)
        {
            // フォーム終了中などで ListView が破棄済みの場合は何もしない
        }
        catch (InvalidOperationException)
        {
            // ハンドル未作成/破棄済みへのアクセス競合時は何もしない
        }
    }

    /// <summary>
    /// ListView が UI 更新可能な状態かを判定する。
    /// </summary>
    /// <param name="target">判定対象の ListView。</param>
    /// <returns>使用可能な場合は <c>true</c>。</returns>
    private static bool IsUsableTarget(ListView? target) =>
        target is not null && !target.IsDisposed && target.IsHandleCreated;

    /// <summary>
    /// ListView にログエントリを追加し、件数が上限を超えた場合は古い行を削除する。
    /// </summary>
    /// <param name="logEvent">追加するログイベント。</param>
    private void AddItem(LogEvent logEvent)
    {
        if (!IsUsableTarget(_target)) return;

        try
        {
            var item = new ListViewItem(logEvent.Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            item.SubItems.Add(logEvent.RenderMessage(CultureInfo.InvariantCulture));
            _target!.Items.Add(item);

            // 最大 200 件を超えたら古い行を削除する
            while (_target.Items.Count > 200)
                _target.Items.RemoveAt(0);

            _target.Items[^1].EnsureVisible();
        }
        catch (ObjectDisposedException)
        {
            // フォーム終了中などで ListView が破棄済みの場合は何もしない
        }
        catch (InvalidOperationException)
        {
            // ハンドル未作成/破棄済みへのアクセス競合時は何もしない
        }
    }
}
