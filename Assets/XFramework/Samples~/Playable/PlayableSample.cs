using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using XFramework.XBootstrap;
using XFramework.XData;
using XFramework.XFileManager;
using XFramework.XLog;
using XFramework.XMessage;
using XFramework.XReactive;
using XFramework.XSave;
using XFramework.XTimer;
using XFramework.XUpdate;

namespace XFramework.Playable
{
    /// <summary>
    /// 零资产的可玩纵切：Bootstrap（Data → Save）+ Data + Save + Message + Reactive +
    /// Timer + File + Log。
    /// <para><b>为什么没有 Asset / UI / Audio</b>：它们需要 YooAsset 的采集配置与面板预制体；
    /// 本示例的目标是「导入后打开场景按 Play 就能跑」，故 UI 只用 IMGUI 做演示壳。模块级用法的
    /// 代码示例见 <c>Samples~/Example</c>（与包一起经 Package Manager 的 Samples 页签导入）。</para>
    /// <para><b>操作</b>：+1 加分并发布消息；Save / Load 使用槽位 0；Write Note 写
    /// <c>AppData/playable/notes.txt</c>；Reset 清内存并删除存档。</para>
    /// </summary>
    public sealed class PlayableSample : MonoBehaviour
    {
        #region Constants

        private const int SaveSlot = 0;
        private const string NotePath = "playable/notes.txt";

        /// <summary>本示例自己的日志分类；第三方可以用 <see cref="LogCategory.Get(string)"/> 自建分类。</summary>
        private static readonly LogCategory Log = LogCategory.Get("PlayableSample");

        #endregion

        #region Private Fields

        private readonly ReactiveProperty<int> _score = new ReactiveProperty<int>(0);
        private readonly ReactiveProperty<int> _seconds = new ReactiveProperty<int>(0);

        private PlayableData _data;
        private IDisposable _scoreSubscription;
        private IDisposable _messageSubscription;
        private TimerHandle _timer;

        private string _status = "starting…";
        private string _note = "(not read yet)";
        private bool _busy;

        #endregion

        #region Unity Lifecycle

        private async void Start()
        {
            try
            {
                // 只登记本示例真正用到的两个阶段：Data(3) → Save(4)。
                // RegisterDefaults() 还会登记 Asset(0)，那需要 YooAsset 的采集配置，故不用。
                Bootstrap.Register(new DataBootstrapStage());
                Bootstrap.Register(new SaveBootstrapStage());
                await Bootstrap.RunAsync(cancellationToken: destroyCancellationToken);

                SaveLoadResult load = await SaveManager.TryLoadAsync(SaveSlot, destroyCancellationToken);
                _data = DataManager.GetOrCreateBlock<PlayableData>();

                // 先赋值再订阅：Subscribe 会立即回放当前值，订阅时 _data 必须已就绪。
                _score.Value = _data.Score;
                _seconds.Value = _data.Seconds;

                _scoreSubscription = _score.Subscribe(value =>
                {
                    _data.Score = value;
                    DataManager.MarkDirty<PlayableData>();
                });
                _messageSubscription = MessageManager.Subscribe<ScoreChangedMessage>(OnScoreChanged);
                _timer = TimerManager.Every(1f, OnSecond, UpdateTimeMode.Scaled, destroyCancellationToken);
                _note = ReadNote();

                _status = load.IsSuccess
                    ? $"loaded slot {SaveSlot} ({load.Status})"
                    : $"new game ({load.Status})";

                LogManager.Info(Log, "示例已启动：score={0}, seconds={1}", _score.Value, _seconds.Value);
            }
            catch (OperationCanceledException)
            {
                // 启动途中对象被销毁：没有需要收尾的东西。
            }
            catch (Exception e)
            {
                _status = $"bootstrap failed: {e.Message}";
                LogManager.Exception(Log, e, "示例启动失败");
            }
        }

        private void OnDestroy()
        {
            _scoreSubscription?.Dispose();
            _messageSubscription?.Dispose();
            _timer.Dispose();
            _score.Dispose();
            _seconds.Dispose();

            // 逆序清理本示例登记的 Data / Save 阶段；已在别处初始化过的服务不会被误拆。
            Bootstrap.Shutdown();
        }

        #endregion

        #region Actions

        private void AddScore()
        {
            _score.Value++;
            MessageManager.Publish(new ScoreChangedMessage(_score.Value));
        }

        private void OnScoreChanged(ScoreChangedMessage message)
        {
            _status = $"message received: score={message.Score}";
            LogManager.Info(Log, "收到 ScoreChangedMessage：{0}", message.Score);
        }

        private void OnSecond()
        {
            _data.Seconds = _seconds.Value + 1;
            _seconds.Value = _data.Seconds;
        }

        private async UniTaskVoid SaveNowAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                await SaveManager.SaveAsync(SaveSlot, destroyCancellationToken);
                _status = $"saved slot {SaveSlot} at {DateTime.Now:HH:mm:ss}";
                LogManager.Info(Log, "已保存槽位 {0}", SaveSlot);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _status = $"save failed: {e.Message}";
                LogManager.Exception(Log, e, "保存失败");
            }
            finally
            {
                _busy = false;
            }
        }

        private async UniTaskVoid LoadNowAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                SaveLoadResult result = await SaveManager.TryLoadAsync(SaveSlot, destroyCancellationToken);
                if (result.IsSuccess)
                {
                    // ApplySnapshot 可能替换块实例，重新取一次。
                    _data = DataManager.GetOrCreateBlock<PlayableData>();
                    _score.Value = _data.Score;
                    _seconds.Value = _data.Seconds;
                }
                _status = $"load: {result.Status}";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _status = $"load failed: {e.Message}";
                LogManager.Exception(Log, e, "读档失败");
            }
            finally
            {
                _busy = false;
            }
        }

        private async UniTaskVoid ResetNowAsync()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                _data = DataManager.GetOrCreateBlock<PlayableData>();
                _data.OnClear();
                _score.Value = 0;
                _seconds.Value = 0;
                await SaveManager.DeleteSlotAsync(SaveSlot, destroyCancellationToken);
                _status = "reset (memory cleared, slot deleted)";
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _status = $"reset failed: {e.Message}";
                LogManager.Exception(Log, e, "复位失败");
            }
            finally
            {
                _busy = false;
            }
        }

        private async UniTaskVoid WriteNoteAsync()
        {
            try
            {
                string note = $"score={_score.Value}, seconds={_seconds.Value}, at={DateTime.Now:HH:mm:ss}";
                await FileManager.WriteAllTextAsync(FileDomain.AppData, NotePath, note, destroyCancellationToken);
                _note = note;
                _status = "note written";
                LogManager.Info(Log, "已写入 {0}", NotePath);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _status = $"write note failed: {e.Message}";
                LogManager.Exception(Log, e, "写文件失败");
            }
        }

        private string ReadNote()
        {
            try
            {
                return FileManager.Exists(FileDomain.AppData, NotePath)
                    ? FileManager.ReadAllText(FileDomain.AppData, NotePath)
                    : "(no note yet)";
            }
            catch (Exception e)
            {
                return $"read failed: {e.Message}";
            }
        }

        #endregion

        #region IMGUI Demo Shell

        // UI 只是演示壳：不用 XUI（它需要 YooAsset 面板预制体），也不追求每帧零分配。
        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(20f, 20f, 420f, 340f), GUI.skin.box);
            GUILayout.Label("XFramework — Playable Sample");
            GUILayout.Space(4f);
            GUILayout.Label($"Score: {_score.Value}");
            GUILayout.Label($"Seconds: {_seconds.Value}");
            GUILayout.Label($"Status: {_status}");
            GUILayout.Label($"Note: {_note}");
            GUILayout.Space(8f);

            if (GUILayout.Button("+1 Score (Message + Reactive)"))
                AddScore();
            if (GUILayout.Button("Save (Data snapshot → file)"))
                SaveNowAsync().Forget();
            if (GUILayout.Button("Load (file → Data snapshot)"))
                LoadNowAsync().Forget();
            if (GUILayout.Button("Write Note (File)"))
                WriteNoteAsync().Forget();
            if (GUILayout.Button("Reset"))
                ResetNowAsync().Forget();

            GUILayout.EndArea();
        }

        #endregion

        #region Sample Types

        /// <summary>分值变化消息：Reactive 负责 UI 绑定，Message 负责跨模块通知。</summary>
        public readonly struct ScoreChangedMessage
        {
            /// <summary>变化后的分值。</summary>
            public readonly int Score;

            /// <summary>构造消息。</summary>
            /// <param name="score">变化后的分值。</param>
            public ScoreChangedMessage(int score)
            {
                Score = score;
            }
        }

        /// <summary>示例数据块：参与 Save 的全量快照。</summary>
        [Serializable]
        public sealed class PlayableData : IDataBlock
        {
            /// <summary>块名（存档载荷里的键）。</summary>
            public string BlockName => "Playable";

            /// <summary>块数据版本。</summary>
            public int DataVersion => 1;

            /// <summary>当前分值。</summary>
            public int Score;

            /// <summary>本次游玩累计秒数。</summary>
            public int Seconds;

            [Serializable]
            private struct Snapshot
            {
                public int score;
                public int seconds;
            }

            /// <summary>导出快照。</summary>
            public object OnSave()
            {
                return new Snapshot { score = Score, seconds = Seconds };
            }

            /// <summary>恒等迁移（本示例只有版本 1）。</summary>
            public object OnMigrate(object saveData, int fromVersion)
            {
                return saveData;
            }

            /// <summary>从快照恢复。</summary>
            public void OnLoad(object data)
            {
                if (data is Snapshot snapshot)
                {
                    Score = snapshot.score;
                    Seconds = snapshot.seconds;
                }
            }

            /// <summary>清空为初始值。</summary>
            public void OnClear()
            {
                Score = 0;
                Seconds = 0;
            }
        }

        #endregion
    }
}
