using System;
using System.Windows;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Force.DeepCloner;

namespace Software_Project_P3_Death_Marble
{
    internal class Connector
    {
        internal class Connection
        {
            // 스태틱(글로벌) 자원 동기화용 락 개체
            private static readonly object _staticLock = new object();
            private static List<Connection> _connections = new List<Connection>();

            // 인스턴스 자원 동기화용 락 개체
            private readonly object _lock = new object();
            private List<(BindableEvent sender, string? receiverName, dynamic data)> _pendingMessages = [];
            private readonly string _name;
            private List<BindableEvent> _events = [];

            // Property
            internal string Name => _name;
            internal List<(BindableEvent sender, string? receiverName, dynamic data)> PendingMessages { get { lock (_lock) { return _pendingMessages.ToList(); } } }
            internal List<BindableEvent> Events { get { lock (_lock) { return _events.ToList(); } } }
            internal static List<Connection> Connections { get { lock (_staticLock) { return _connections.ToList(); } } }

            // Get Connection (Static) - 멀티스레드 안전하게 조회
            internal static Connection? GetConnection(string name)
            {
                lock (_staticLock)
                {
                    return _connections.FirstOrDefault(c => c.Name == name);
                }
            }

            // Add pendingMessage
            private void AddPendingMessage(BindableEvent sender, string? receiverName, dynamic data)
            {
                lock (_lock)
                {
                    _pendingMessages.Add((sender, receiverName, ((object)data).DeepClone()));
                }
            }

            // Put and remove BindableEvent in Connection when same category
            internal BindableEvent AddEvent()
            {
                lock (_lock)
                {
                    BindableEvent e = new BindableEvent(this);
                    _events.Add(e);
                    return e;
                }
            }

            internal void RemoveEvent(BindableEvent e)
            {
                bool shouldDestroy = false;
                lock (_lock)
                {
                    _events.Remove(e);
                    if (_events.Count <= 0)
                    {
                        shouldDestroy = true;
                    }
                }

                if (shouldDestroy)
                {
                    Destroy();
                }
            }

            // flush Pending Messages
            internal void FlushPendingMessages(BindableEvent receiver)
            {
                List<(BindableEvent sender, string? receiverName, dynamic data)> snapshot;
                lock (_lock)
                {
                    if (_pendingMessages.Count == 0) return;
                    snapshot = _pendingMessages.ToList(); // 교착 상태 방지를 위해 락 범위 최소화 및 스냅샷 생성
                }

                // 역순으로 순회하며 처리
                for (int i = snapshot.Count - 1; i >= 0; i--)
                {
                    var msg = snapshot[i];
                    if (msg.sender == receiver) continue;

                    // 전체 발송이었거나, 이름이 일치하는 수신자인 경우 전달 후 대기열에서 제거
                    if (msg.receiverName == null || msg.receiverName == receiver.Name)
                    {
                        InvokeSafe(receiver, msg.sender.Name, ((object)msg.data).DeepClone());

                        lock (_lock)
                        {
                            // 그 사이 다른 스레드에서 대기열이 변했을 수 있으므로 인덱스 기반이 아닌 동등성 비교로 제거
                            _pendingMessages.Remove(msg);
                        }
                    }
                }
            }

            // Protected Send Process When callback handle XAML
            private void InvokeSafe(BindableEvent target, string senderName, dynamic data)
            {
                if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
                    Application.Current.Dispatcher.InvokeAsync(() => target.OnInvoke?.Invoke(senderName, data));
                else
                    target.OnInvoke?.Invoke(senderName, data);
            }

            // Send Data to target BindableEvent
            internal void Send(BindableEvent sender, dynamic data)
            {
                bool delivered = false;
                List<BindableEvent> targets;

                lock (_lock)
                {
                    targets = _events.ToList(); // 락이 걸린 상태에서 타겟 목록 추출
                }

                foreach (BindableEvent target in targets)
                {
                    if (target != sender && target.HasHandler)
                    {
                        InvokeSafe(target, sender.Name, ((object)data).DeepClone());
                        delivered = true;
                    }
                }

                if (!delivered)
                    AddPendingMessage(sender, null, data);
            }

            internal void Send(BindableEvent sender, string receiverName, dynamic data)
            {
                bool delivered = false;
                List<BindableEvent> targets;

                lock (_lock)
                {
                    targets = _events.ToList();
                }

                foreach (BindableEvent target in targets)
                {
                    if (target != sender && target.Name == receiverName && target.HasHandler)
                    {
                        InvokeSafe(target, sender.Name, ((object)data).DeepClone());
                        delivered = true;
                    }
                }

                if (!delivered)
                    AddPendingMessage(sender, receiverName, data);
            }

            // Remove Connection
            private void Destroy()
            {
                lock (_lock)
                {
                    _events.Clear();
                }
                lock (_staticLock)
                {
                    _connections.Remove(this);
                }
            }

            internal Connection(string name)
            {
                _name = name;
                lock (_staticLock)
                {
                    _connections.Add(this);
                }
            }
        }

        internal class BindableEvent
        {
            private readonly Connection _connection;
            private string _name = "";
            private Action<string, dynamic?>? _onInvoke = null;
            private readonly object _propertyLock = new object(); // 개별 프로퍼티 동기화용 락

            // Property
            public string Name
            {
                get { lock (_propertyLock) return _name; }
                set { lock (_propertyLock) _name = value?.Trim() ?? ""; }
            }

            public Connection Connection => _connection;

            public Action<string, dynamic?>? OnInvoke
            {
                get { lock (_propertyLock) return _onInvoke; }
                set
                {
                    lock (_propertyLock)
                    {
                        _onInvoke = value;
                    }
                    // 대기 메시지 플러시는 락 밖에서 실행하여 데드락 예방
                    if (value != null) Connection.FlushPendingMessages(this);
                }
            }

            internal bool HasHandler => OnInvoke != null;

            // Send Data to Own Connection
            public void Fire(dynamic data)
            {
                Connection.Send(this, data);
            }

            public void Fire(string receiverName, dynamic data)
            {
                Connection.Send(this, receiverName, data);
            }

            public void Fire(List<string> receiverNames, dynamic data)
            {
                var clonedData = ((object)data).DeepClone();
                foreach (string receiverName in receiverNames)
                {
                    Connection.Send(this, receiverName, clonedData);
                }
            }

            // Remove BindableEvent
            public void Destroy()
            {
                Connection.RemoveEvent(this);
            }

            internal BindableEvent(Connection c)
            {
                _connection = c;
            }
        }

        public static BindableEvent GetBindableEvent(string name)
        {
            // 스태틱 메서드 내부에서 동시 생성 크래시 방지
            Connection? c = Connection.GetConnection(name);
            if (c == null)
            {
                c = new Connection(name);
            }
            return c.AddEvent();
        }
    }
}