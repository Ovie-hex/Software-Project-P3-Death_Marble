using System;
using System.Windows;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Force.DeepCloner;
using static Software_Project_P3_Death_Marble.Connector;

namespace Software_Project_P3_Death_Marble
{
    internal class Connector
    {
        internal class Connection
        {
            private static List<Connection> _connections = new List<Connection>();
            private List<(BindableEvent sender, string? receiverName, dynamic data)> _pendingMessages = [];
            private readonly string _name;
            private List<BindableEvent> _events = [];

            // Property
            internal string name => _name;
            internal List<(BindableEvent sender, string? receiverName, dynamic data)> pendingMessages => _pendingMessages;
            internal List<BindableEvent> events => _events;
            internal static List<Connection> connections => _connections;


            // Get Connection (Static)
            internal static Connection? getConnection(string name)
            {
                return connections.FirstOrDefault(c => c.name == name);
            }

            // Add pendingMessage
            private void addPendingMessage(BindableEvent sender, string receiverName, dynamic data)
            {
                    _pendingMessages.Add((sender, receiverName, ((object)data).DeepClone()));
            }

            // Put and remove BindableEvent in Connection when same category
            internal BindableEvent addEvent()
            {
                BindableEvent e = new BindableEvent(this);
                events.Add(e);
                return e;
            }
            internal void removeEvent(BindableEvent e)
            {
                events.Remove(e);
                if (events.Count <= 0)
                {
                    destroy();
                }
            }

            // flush Pending Messages
            /*
             *  AI가 순수 작성한 부분임.. ( _pendingMessages, hasHandler도 마찬가지 )
             *  원래 문제 2번에서 각 모듈의 단일 쓰레드로 이벤트 전달을 하는 경우 에러가 발생하였고
             *  이를 해결하기 위해 대기중인 메시지를 만들어서 함수가 연결될 때 즉시 이벤트 연결이 됨
             */
            internal void flushPendingMessages(BindableEvent receiver)
            {
                if (_pendingMessages.Count == 0) return;

                for (int i = _pendingMessages.Count - 1; i >= 0; i--)
                {
                    var msg = _pendingMessages[i];
                    if (msg.sender == receiver) continue;

                    // 전체 발송이었거나, 이름이 일치하는 수신자인 경우 전달 후 대기열에서 제거
                    if (msg.receiverName == null || msg.receiverName == receiver.name)
                    {
                        InvokeSafe(receiver, msg.sender.name, ((object)msg.data).DeepClone());
                        _pendingMessages.RemoveAt(i);
                    }
                }
            }


            // Protected Send Process When callback handle XAML
            /*
             *  AI가 순수 작성한 부분임..
             *  XAML 소유권이 고유한 UI 쓰레드(Dispatcher)가 가지고 있어서
             *  콜백 함수 쓰레드가 XAML 다루지 못하게 에러를 발생한다는 거임 자세한것은 ㅁㄹ
             */
            private void InvokeSafe(BindableEvent target, string senderName, dynamic data)
            {
                if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
                    Application.Current.Dispatcher.InvokeAsync(() => target.onInvoke?.Invoke(senderName, data));
                else
                    target.onInvoke?.Invoke(senderName, data);
            }

            // Send Data to target BindableEvent
            internal void send(BindableEvent sender, dynamic data)
            {
                bool delivered = false;

                foreach (BindableEvent target in events.ToList())
                {
                    if (target != sender && target.hasHandler)
                    {
                        InvokeSafe(target, sender.name, ((object)data).DeepClone());
                        delivered = true;
                    }
                }
                if(!delivered)
                    addPendingMessage(sender, null, data);
            }

            internal void send(BindableEvent sender, string receiverName, dynamic data)
            {
                bool delivered = false;

                foreach (BindableEvent target in events.ToList())
                {
                    if (target != sender && target.name == receiverName && target.hasHandler)
                    {
                        InvokeSafe(target, sender.name, ((object)data).DeepClone());
                        delivered = true;
                    }
                }
                if (!delivered)
                    addPendingMessage(sender, receiverName, data);
            }

            // Remove Connection
            private void destroy()
            {
                events.Clear();
                Connection.connections.Remove(this);
            }

            internal Connection(string name)
            {
                _name = name;
                connections.Add(this);
            }
        }

        internal class BindableEvent
        {
            private readonly Connection _connection;
            private string _name = "";
            private Action<string, dynamic>? _onInvoke = null;

            // Property
            public string name { get => _name; set => _name = value?.Trim() ?? ""; }
            public Connection connection => _connection;
            public Action<string, dynamic>? onInvoke{ 
                get => _onInvoke;
                set {
                    _onInvoke = value;
                    if (_onInvoke != null)
                        connection.flushPendingMessages(this);
                } 
            }
            internal bool hasHandler => _onInvoke != null;

            // Send Data to Own Connection
            public void fire(dynamic data)
            {
                connection.send(this, data);
            }

            public void fire(string receiverName, dynamic data)
            {
                connection.send(this, receiverName, data);
            }

            public void fire(List<string> receiverNames, dynamic data)
            {
                foreach (string receiverName in receiverNames) {
                    fire(receiverName, data);
                }
            }

            // Remove BindableEvent
            public void destroy()
            {
                connection.removeEvent(this); 
            }

            internal BindableEvent(Connection c)
            {
                _connection = c;
            }

        }

        public static BindableEvent getBindableEvent(string name)
        {
            Connection? c = Connection.getConnection(name);
            if (c == null)
                c = new Connection(name);

            return c.addEvent();
        }
    }
}
