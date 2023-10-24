using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Utility.LogRecorder
{
	public class DynamicCircularBuffer<T>
	{
		private T[] arr;
		private int startIndex;

		public int Count { get; private set; }

		public int Capacity
		{
			get { return arr.Length; }
		}

		public T this[int index]
		{
			get { return arr[(startIndex + index) % arr.Length]; }
			set { arr[(startIndex + index) % arr.Length] = value; }
		}

		public DynamicCircularBuffer(int initialCapacity = 2)
		{
			arr = new T[initialCapacity];
		}

		// ReSharper disable once CognitiveComplexity
		public void Add(T value)
		{
			if (Count >= arr.Length)
			{
				var prevSize = arr.Length;
				var newSize =
					prevSize > 0
						? prevSize * 2
						: 2; // Size must be doubled (at least), or the shift operation below must consider IndexOutOfRange situations

				Array.Resize(ref arr, newSize);

				if (startIndex > 0)
				{
					if (startIndex <= (prevSize - 1) / 2)
					{
						// Move elements [0,startIndex) to the end
						for (int i = 0; i < startIndex; i++)
						{
							arr[i + prevSize] = arr[i];
						#if RESET_REMOVED_ELEMENTS
							arr[i] = default(T);
						#endif
						}
					}
					else
					{
						// Move elements [startIndex,prevSize) to the end
						var delta = newSize - prevSize;
						for (var i = prevSize - 1; i >= startIndex; i--)
						{
							arr[i + delta] = arr[i];
						#if RESET_REMOVED_ELEMENTS
							arr[i] = default(T);
						#endif
						}

						startIndex += delta;
					}
				}
			}

			this[Count++] = value;
		}

		public T RemoveFirst()
		{
			var element = arr[startIndex];
		#if RESET_REMOVED_ELEMENTS
			arr[startIndex] = default(T);
		#endif

			if (++startIndex >= arr.Length)
				startIndex = 0;

			Count--;
			return element;
		}

		public T RemoveLast()
		{
			var index = (startIndex + Count - 1) % arr.Length;
			var element = arr[index];
		#if RESET_REMOVED_ELEMENTS
			arr[index] = default(T);
		#endif

			Count--;
			return element;
		}
	}
	
	[Flags]
	public enum DebugLogFilter
	{
		Info = 1 << 0,
		Warning = 1 << 1,
		Error = 1 << 2,
		Exception = 1 << 3,
		All = Info | Warning | Error | Exception
	}
	
	public class DebugLogIndexList<T>
	{
		private T[] indices;
		private int size;

		public int Count { get { return size; } }
		public T this[int index]
		{
			get { return indices[index]; }
			set { indices[index] = value; }
		}

		public DebugLogIndexList()
		{
			indices = new T[64];
			size = 0;
		}

		public void Add( T value )
		{
			if( size == indices.Length )
				Array.Resize( ref indices, size * 2 );

			indices[size++] = value;
		}

		public void Clear()
		{
			size = 0;
		}

		public int IndexOf( T value )
		{
			return Array.IndexOf( indices, value );
		}
	}

	public struct QueuedDebugLogEntry
	{
		public readonly string LogString;
		public readonly string StackTrace;
		public readonly LogType LogType;

		public QueuedDebugLogEntry(string logString, string stackTrace, LogType logType)
		{
			LogString = logString;
			StackTrace = stackTrace;
			LogType = logType;
		}
	}

	public class DebugLogEntry : IEquatable<DebugLogEntry>
	{
		public string LogString;
		public string StackTrace;
		public LogType LogType;

		private string completeLog;

		public void Initialize(string logString, string stackTrace, LogType logType)
		{
			LogString = logString;
			StackTrace = stackTrace;
			LogType = logType;

			completeLog = null;
		}

		public bool Equals(DebugLogEntry other)
		{
			return other != null && LogString == other.LogString && StackTrace == other.StackTrace &&
			       LogType == other.LogType;
		}

		public override string ToString()
		{
			return completeLog ??= $"({LogType}){LogString}\n{StackTrace}";
		}

		// Credit: https://stackoverflow.com/a/19250516/2373034
		[SuppressMessage("ReSharper", "NonReadonlyMemberInGetHashCode")]
		public override int GetHashCode()
		{
			int hashValue;

			unchecked
			{
				hashValue = 17;
				hashValue = hashValue * 23 + (LogString == null ? 0 : LogString.GetHashCode());
				hashValue = hashValue * 23 + (StackTrace == null ? 0 : StackTrace.GetHashCode());
				hashValue = hashValue * 23 + LogType.GetHashCode();
			}

			return hashValue;
		}
	}

	[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
	public readonly struct DebugLogEntryTimestamp
	{
		public readonly DateTime DateTime;
		public readonly float ElapsedSeconds;
		public readonly int FrameCount;

		public DebugLogEntryTimestamp(DateTime dateTime, float elapsedSeconds, int frameCount)
		{
			DateTime = dateTime;
			ElapsedSeconds = elapsedSeconds;
			FrameCount = frameCount;
		}

		public string GetTimeString()
		{
			return $"{DateTime:HH:mm:ss}";
		}
		
		public string GetElapsedTime()
		{
			return $"{ElapsedSeconds:F1}s";
		}
		
		public string GetFrameCount()
		{
			return $"#{FrameCount}";
		}

		public override string ToString()
		{
			return GetTimeString();
		}

		public string ToFullString()
		{
			//12:23:34 1.2s at #123
			return $"{GetTimeString()} {GetElapsedTime()} at {GetFrameCount()}";
		}
	}

	public class DebugLogRecorder : MonoBehaviour
	{
		[Header("Option")]
		[SerializeField]
		private DebugLogFilter receiveLogFilter = DebugLogFilter.All;

		[SerializeField]
		[Tooltip("활성화하면 로그 도착 시간이 기록되고 로그 확장 시 표시됩니다.")]
		private bool captureLogTimestamps = false;
		
		[SerializeField]
		private bool isFullLog = false;

	#if UNITY_EDITOR
		[Header("Editor Option")]
		[SerializeField]
		[Tooltip("에디터를 사용중 일때도 실시간 로그를 파일에 저장합니다.")]
		private bool editorSaveLog = false;
	#endif

		// 고유한 디버그 항목 목록(중복 항목은 보관되지 않음)
		private List<DebugLogEntry> collapsedLogEntries;
		private List<DebugLogEntryTimestamp> collapsedLogEntriesTimestamps;

		// 축소된 LogEntries에 로그가 이미 있는지 빠르게 찾기 위한 사전
		private Dictionary<DebugLogEntry, int> collapsedLogEntriesMap;

		// collapsedLogEntries가 수신되는 순서
		// (중복 항목은 동일한 색인(값)을 가짐)
		private DebugLogIndexList<int> uncollapsedLogEntriesIndices;
		private DebugLogIndexList<DebugLogEntryTimestamp> uncollapsedLogEntriesTimestamps;

		// 이 프레임에서 로그 목록 보기를 업데이트해야 하는지 여부
		private bool shouldUpdateRecycledListView = false;

		// Update-loop에 등록해야 하는 로그
		private DynamicCircularBuffer<QueuedDebugLogEntry> queuedLogEntries;
		private DynamicCircularBuffer<DebugLogEntryTimestamp> queuedLogEntriesTimestamps;
		private object logEntriesLock;

		// 메모리 효율성을 위한 풀
		private List<DebugLogEntry> pooledLogEntries;

		// DateTime.UtcNow에서 DateTime.Now의 오프셋
		private TimeSpan localTimeUtcOffset;

		private string filePath;

		// 메인 스레드에서 마지막으로 기록된 Time.realtimeSinceStartup 및 Time.frameCount값
		private float lastElapsedSeconds;
		private int lastFrameCount;

		private DebugLogEntryTimestamp dummyLogEntryTimestamp;

	#if UNITY_EDITOR
		private bool isQuittingApplication;
	#endif

		private static DebugLogRecorder instance;

		private void Awake()
		{
			if (!instance)
			{
				instance = this;
				DontDestroyOnLoad(gameObject);
			}
			else if (instance != this)
			{
				Destroy(gameObject);
				return;
			}

		#if DEVELOPMENT_BUILD
			Debug.Log($"[Development Build] {Application.identifier}");
		#else
			Debug.Log($"[Release Build] {Application.identifier}");
		#endif

			pooledLogEntries = new List<DebugLogEntry>(16);
			queuedLogEntries = new DynamicCircularBuffer<QueuedDebugLogEntry>(Mathf.Clamp(256, 16, 4096));

			logEntriesLock = new object();

			collapsedLogEntries = new List<DebugLogEntry>(128);
			collapsedLogEntriesMap = new Dictionary<DebugLogEntry, int>(128);
			uncollapsedLogEntriesIndices = new DebugLogIndexList<int>();

			if (captureLogTimestamps)
			{
				collapsedLogEntriesTimestamps = new List<DebugLogEntryTimestamp>(128);
				uncollapsedLogEntriesTimestamps = new DebugLogIndexList<DebugLogEntryTimestamp>();

				lock (logEntriesLock)
				{
					queuedLogEntriesTimestamps =
						new DynamicCircularBuffer<DebugLogEntryTimestamp>(queuedLogEntries.Capacity);
				}
			}

			localTimeUtcOffset = DateTime.Now - DateTime.UtcNow;
			dummyLogEntryTimestamp = new DebugLogEntryTimestamp();

			var dayString = DateTime.Now.ToString("yyyy-dd-MM(HH mm ss)"); // 2021-01-01(00:00:00)
			filePath = Path.Combine(Application.persistentDataPath, $"TempLog_{dayString}.txt");

			Debug.Log($"LogRecorder: {filePath}");

		#if UNITY_EDITOR && UNITY_2018_1_OR_NEWER
			Application.quitting -= OnApplicationQuitting;
			Application.quitting += OnApplicationQuitting;
		#endif
		}

		private void OnEnable()
		{
			if (instance != this)
				return;

			Application.logMessageReceivedThreaded -= ReceivedLog;
			Application.logMessageReceivedThreaded += ReceivedLog;

			// Debug.LogAssertion("assert");
			// Debug.LogError("error");
			// Debug.LogException(new EndOfStreamException());
			// Debug.LogWarning("warning");
			// Debug.Log("log");
			//
			// throw new NotImplementedException();
			//
			// Debug.Log("log2");
		}

		private void OnDisable()
		{
			if (instance != this)
				return;

			Application.logMessageReceivedThreaded -= ReceivedLog;
		}

		private void OnDestroy()
		{
		#if UNITY_EDITOR && UNITY_2018_1_OR_NEWER
			Application.quitting -= OnApplicationQuitting;
		#endif
		}

	#if UNITY_EDITOR
	#if UNITY_2018_1_OR_NEWER
		private void OnApplicationQuitting()
	#else
		private void OnApplicationQuit()
	#endif
		{
			isQuittingApplication = true;
		}
	#endif

		private void Update()
		{
			lastElapsedSeconds = Time.realtimeSinceStartup;
			lastFrameCount = Time.frameCount;
		}

		private bool isFirst = true;

		private void LateUpdate()
		{
		#if UNITY_EDITOR
			if (isQuittingApplication)
				return;
		#endif

			var numberOfLogsToProcess = queuedLogEntries.Count;
			ProcessQueuedLogs(numberOfLogsToProcess);

		#if UNITY_EDITOR
			if (!editorSaveLog) // 에디터에서는 실시간으로 로그를 저장하지 않습니다.
			{
				shouldUpdateRecycledListView = false;
				return;
			}
		#endif

			if (shouldUpdateRecycledListView)
			{
				if (isFirst)
				{
					for (var i = 0; i < uncollapsedLogEntriesIndices.Count; i++)
					{
						WriteLog(i);
					}

					isFirst = false;
				}
				else
				{
					WriteLog(uncollapsedLogEntriesIndices[^1]);
				}

				shouldUpdateRecycledListView = false;
			}
		}

		// ReSharper disable once CognitiveComplexity
		private void ReceivedLog(string logString, string stackTrace, LogType logType)
		{
		#if UNITY_EDITOR
			if (isQuittingApplication)
				return;
		#endif

			switch (logType)
			{
				case LogType.Log:
					if (!receiveLogFilter.HasFlag(DebugLogFilter.Info))
						return;
					break;
				case LogType.Warning:
					if (!receiveLogFilter.HasFlag(DebugLogFilter.Warning))
						return;
					break;
				case LogType.Error:
					if (!receiveLogFilter.HasFlag(DebugLogFilter.Error))
						return;
					break;
				case LogType.Assert:
				case LogType.Exception:
					if (!receiveLogFilter.HasFlag(DebugLogFilter.Exception))
						return;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(logType), logType, null);
			}

			var queuedLogEntry = new QueuedDebugLogEntry(logString, stackTrace, logType);
			DebugLogEntryTimestamp queuedLogEntryTimestamp;
			if (queuedLogEntriesTimestamps != null)
			{
				var dateTime = DateTime.UtcNow + localTimeUtcOffset;
				queuedLogEntryTimestamp = new DebugLogEntryTimestamp(dateTime, lastElapsedSeconds, lastFrameCount);
			}
			else
			{
				queuedLogEntryTimestamp = dummyLogEntryTimestamp;
			}

			lock (logEntriesLock)
			{
				queuedLogEntries.Add(queuedLogEntry);
				queuedLogEntriesTimestamps?.Add(queuedLogEntryTimestamp);
			}
		}

		// 대기 중인 로그 대기열에서 대기 중인 여러 로그 처리
		private void ProcessQueuedLogs(int numberOfLogsToProcess)
		{
			for (var i = 0; i < numberOfLogsToProcess; i++)
			{
				QueuedDebugLogEntry logEntry;
				DebugLogEntryTimestamp timestamp;
				lock (logEntriesLock)
				{
					logEntry = queuedLogEntries.RemoveFirst();
					timestamp = queuedLogEntriesTimestamps?.RemoveFirst() ?? dummyLogEntryTimestamp;
				}

				ProcessLog(logEntry, timestamp);
			}
		}

		// 콘솔에 로그 항목 표시
		private void ProcessLog(QueuedDebugLogEntry queuedLogEntry, DebugLogEntryTimestamp timestamp)
		{
			DebugLogEntry logEntry;
			if (pooledLogEntries.Count > 0)
			{
				logEntry = pooledLogEntries[^1];
				pooledLogEntries.RemoveAt(pooledLogEntries.Count - 1);
			}
			else
			{
				logEntry = new DebugLogEntry();
			}

			logEntry.Initialize(queuedLogEntry.LogString, queuedLogEntry.StackTrace, queuedLogEntry.LogType);

			// 이 항목이 중복인지 확인하십시오(예: 이전에 수신됨).
			var isEntryInCollapsedEntryList = collapsedLogEntriesMap.TryGetValue(logEntry, out var logEntryIndex);
			if (!isEntryInCollapsedEntryList)
			{
				logEntryIndex = collapsedLogEntries.Count;
				collapsedLogEntries.Add(logEntry);
				collapsedLogEntriesMap[logEntry] = logEntryIndex;

				collapsedLogEntriesTimestamps?.Add(timestamp);
			}
			else
			{
				// 중복이므로 중복 로그 항목을 풀링하고
				// 원래 디버그 항목의 접힌 횟수를 증가시킵니다.
				pooledLogEntries.Add(logEntry);

				if (collapsedLogEntriesTimestamps != null)
					collapsedLogEntriesTimestamps[logEntryIndex] = timestamp;
			}

			// 고유한 디버그 항목의 인덱스를 목록에 추가합니다.
			// 디버그 항목이 수신된 순서를 저장합니다.
			uncollapsedLogEntriesIndices.Add(logEntryIndex);

			// 원하는 경우 로그의 타임스탬프 기록
			uncollapsedLogEntriesTimestamps?.Add(timestamp);

			shouldUpdateRecycledListView = true;
		}

		private bool isWritingLog = false;
		private readonly List<string> waitLogList = new();

		private void WriteLog(int index)
		{
			var entry = collapsedLogEntries[uncollapsedLogEntriesIndices[index]];
			var logString = entry.ToString();

			if (uncollapsedLogEntriesTimestamps != null)
			{
				var logEntriesTimestamp = uncollapsedLogEntriesTimestamps[index];
				var timeString = isFullLog ? logEntriesTimestamp.ToFullString() : logEntriesTimestamp.ToString();
				logString = $"[{timeString}]: {logString}";
			}

			WriteLog(logString);
		}

		private void WriteLog(string log)
		{
			waitLogList.Add(log);

			if (isWritingLog)
				return;

			isWritingLog = true;

			while (waitLogList.Count > 0)
			{
				var allLog = string.Join("\n", waitLogList);
				waitLogList.Clear();

				var thread = new Thread(() => { File.AppendAllText(filePath, allLog + "\n"); });
				thread.Start();

				thread.Join();
			}

			isWritingLog = false;
		}

		private string GetAllLogs()
		{
			ProcessQueuedLogs(queuedLogEntries.Count); //보류 중인 모든 로그를 처리

			var count = uncollapsedLogEntriesIndices.Count;
			var length = 0;
			var newLineLength = Environment.NewLine.Length;
			for (var i = 0; i < count; i++)
			{
				var entry = collapsedLogEntries[uncollapsedLogEntriesIndices[i]];
				length += entry.LogString.Length + entry.StackTrace.Length + newLineLength * 3;
			}

			if (uncollapsedLogEntriesTimestamps != null)
				length += count * 12;

			length += 100;

			var sb = new StringBuilder(length);
			for (var i = 0; i < count; i++)
			{
				var entry = collapsedLogEntries[uncollapsedLogEntriesIndices[i]];

				if (uncollapsedLogEntriesTimestamps != null)
				{
					var logEntriesTimestamp = uncollapsedLogEntriesTimestamps[i];
					sb.Append($"{logEntriesTimestamp.ToString()}: ");
				}

				sb.AppendLine($"{entry.LogString}\n{entry.StackTrace}\n");
			}

			return sb.ToString();
		}

	#if ODIN_INSPECTOR
		[Sirenix.OdinInspector.Button]
	#endif
		public static void SaveLogsToFile()
		{
			if (!instance)
			{
				Debug.LogError("DebugLogRecorder instance is null");
				return;
			}

			var dayString = DateTime.Now.ToString("yyyy-dd-MM(HH mm ss)");
			var path = Path.Combine(Application.persistentDataPath, $"AllLog_{dayString}.txt");

			File.WriteAllText(path, instance.GetAllLogs());
			Debug.Log("Logs saved to: " + path);
		}
	}
}
