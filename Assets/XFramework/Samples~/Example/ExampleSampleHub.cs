using UnityEngine;
using XFramework.XTimer;

namespace XFramework.Example
{
    /// <summary>
    /// Example 场景的 IMGUI 操作面板：把 Timer 与 Audio 示例的公开入口接到按钮上。
    /// <para>示例组件本身主要是往 Console 打日志；这个 HUD 让「导入后按 Play」能看到可操作的反馈，
    /// 也演示运行期如何读 Timer 的冷却进度与活跃定时器数。</para>
    /// <para>它只是示例外壳：真实项目应把这些调用接到自己的 UI 上。</para>
    /// </summary>
    public sealed class ExampleSampleHub : MonoBehaviour
    {
        #region Inspector

        [SerializeField] private TimerSample _timer;
        [SerializeField] private AudioSample _audio;

        private GUIStyle _richLabel;

        #endregion

        #region Lifecycle

        private void Awake()
        {
            // 场景生成器会写好引用；手挂组件时则按类型找一下，少一步 Inspector 操作。
            if (_timer == null)
                _timer = FindAnyObjectByType<TimerSample>();
            if (_audio == null)
                _audio = FindAnyObjectByType<AudioSample>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 430f, 400f), GUI.skin.box);
            GUILayout.Label("<b>XFramework Example</b>", RichLabel());
            GUILayout.Label("Reactive / Message / Audio / Timer 的演示日志输出在 Console。", RichLabel());
            GUILayout.Space(8f);

            if (_timer != null)
            {
                GUILayout.Label(
                    $"Timer：tick={_timer.TickCount}  active={_timer.ActiveCount}  " +
                    $"cooldown={_timer.CooldownProgress:P0}",
                    RichLabel());

                if (GUILayout.Button("Use Skill（冷却 8 秒）"))
                    _timer.UseSkill();
            }
            else
            {
                GUILayout.Label("场景里没有 TimerSample。", RichLabel());
            }

            GUILayout.Space(8f);

            if (_audio != null)
            {
                GUILayout.Label(
                    $"Audio：ready={_audio.IsReady}  voices={_audio.ActiveVoices}",
                    RichLabel());

                GUI.enabled = _audio.IsReady;
                if (GUILayout.Button("Play UI Click"))
                    _audio.PlayUIClick();
                GUI.enabled = true;

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Pause Audio"))
                    _audio.SetPaused(true);
                if (GUILayout.Button("Resume Audio"))
                    _audio.SetPaused(false);
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("场景里没有 AudioSample。", RichLabel());
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Cancel Timers（一次性，停表后不再恢复）"))
                TimerManager.CancelAll();

            GUILayout.EndArea();
        }

        #endregion

        #region Helpers

        private GUIStyle RichLabel()
        {
            // OnGUI 每帧会走 Layout/Repaint 多次；皮肤样式只建一次。
            return _richLabel ??= new GUIStyle(GUI.skin.label) { richText = true };
        }

        #endregion
    }
}
