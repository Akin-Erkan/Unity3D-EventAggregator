﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class EventAggregator : AbstractEventAggregator
{
    private class WeakEventHandler
    {
        private readonly WeakReference _weakReference;
        private readonly Dictionary<Type, MethodInfo> _supportedHandlers;

        public bool IsDead
        {
            get { return _weakReference.Target == null; }
        }

        public WeakEventHandler(object handler)
        {
            _weakReference = new WeakReference(handler);
            _supportedHandlers = new Dictionary<Type, MethodInfo>();

            var interfaces = handler.GetType().GetInterfaces()
                .Where(x => typeof(IHandle).IsAssignableFrom(x) && x.IsGenericType);

            foreach (var @interface in interfaces)
            {
                var type = @interface.GetGenericArguments()[0];
                var method = @interface.GetMethod("Handle");
                _supportedHandlers[type] = method;
            }
        }

        public bool Matches(object instance)
        {
            return _weakReference.Target == instance;
        }

        public bool Handle(Type messageType, object message)
        {
            var target = _weakReference.Target;
            if (target == null)
            {
                return false;
            }

            foreach (var pair in _supportedHandlers)
            {
                if (pair.Key.IsAssignableFrom(messageType))
                {
                    var result = pair.Value.Invoke(target, new[] { message });
                    if (result != null)
                    {
                        HandlerResultProcessing(target, result);
                    }
                }
            }

            return true;
        }

        public bool Handles(Type messageType)
        {
            return _supportedHandlers.Any(pair => pair.Key.IsAssignableFrom(messageType));
        }
    }

    private readonly List<WeakEventHandler> _handlers;

    public static Action<object, object> HandlerResultProcessing = (target, result) => { };

    public EventAggregator()
    {
        _handlers = new List<WeakEventHandler>();
    }

    public override bool HandlerExistsFor(Type messageType)
    {
        return _handlers.Any(handler => handler.Handles(messageType) & !handler.IsDead);
    }

    public override void Subscribe(object subscriber)
    {
        if (subscriber == null)
        {
            throw new ArgumentNullException("subscriber");
        }

        lock (_handlers)
        {
            if (!_handlers.Any(x => x.Matches(subscriber)))
            {
                _handlers.Add(new WeakEventHandler(subscriber));
            }
        }
    }

    public void Unsubscribe(object subscriber)
    {
        if (subscriber == null)
        {
            throw new ArgumentNullException("subscriber");
        }

        lock (_handlers)
        {
            if (_handlers.Any(x => x.Matches(subscriber)))
            {
                WeakEventHandler toRemove = null;
                foreach (WeakEventHandler weakEventHandler in _handlers)
                {
                    if (weakEventHandler.Matches(subscriber))
                    {
                        toRemove = weakEventHandler;
                    }
                }

                if (toRemove != null)
                {
                    _handlers.Remove(toRemove);
                }
            }
        }
    }

    /// <summary>
    /// Publish a message on the current thread
    /// </summary>
    /// <param name="message"></param>
    public override void Publish(object message)
    {
        Publish(message, action => action());
    }

    private void Publish(object message, Action<Action> marshal)
    {
        if (message == null)
        {
            throw new ArgumentNullException("message");
        }

        if (marshal == null)
        {
            throw new ArgumentNullException("marshal");
        }

        WeakEventHandler[] toNotify;
        lock (_handlers)
        {
            toNotify = _handlers.ToArray();
        }

        marshal(() =>
        {
            var messageType = message.GetType();

            var dead = toNotify
                .Where(handler => !handler.Handle(messageType, message))
                .ToList();

            if (dead.Any())
            {
                lock (_handlers)
                {
                    foreach (var handler in dead)
                    {
                        _handlers.Remove(handler);
                    }
                }
            }
        });
    }

    #region Singleton
    private static EventAggregator _eventAggregator;
    public static EventAggregator Instance
    {
        get
        {
            var _ = _eventAggregator ? _eventAggregator : (_eventAggregator = FindObjectOfType<EventAggregator>()) ??
                                                          (_eventAggregator = new GameObject("EventAggregator").AddComponent<EventAggregator>());
            DontDestroyOnLoad(_);
            return _;
        }
        private set { _eventAggregator = value; }
    }
    #endregion
}
