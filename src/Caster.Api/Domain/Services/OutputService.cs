// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Caster.Api.Utilities.Synchronization;
using Nito.AsyncEx;

namespace Caster.Api.Domain.Services
{
    public interface IOutputService
    {
        Output GetOutput(Guid objectId);
        Output GetOrAddOutput(Guid objectId);
        void RemoveOutput(Guid objectId);
    }

    public class OutputService : IOutputService
    {
        private ConcurrentDictionary<Guid, Output> _outputs = new ConcurrentDictionary<Guid, Output>();

        public OutputService()
        {
        }

        public Output GetOutput(Guid objectId)
        {
            Output output;

            if (_outputs.TryGetValue(objectId, out output))
            {
                return output;
            }
            else
            {
                return null;
            }
        }

        public Output GetOrAddOutput(Guid objectId)
        {
            return _outputs.GetOrAdd(objectId, new Output());
        }

        public void RemoveOutput(Guid objectId)
        {
            _outputs.Remove(objectId, out _);
        }
    }

    public class Output
    {
        private Object _lock { get; } = new Object();

        private string _content = string.Empty;
        public string Content
        {
            get
            {
                lock (_lock)
                {
                    return _content;
                }
            }
        }

        private bool _complete;
        public bool Complete
        {
            get
            {
                lock (_lock)
                {
                    return _complete;
                }
            }
        }

        private List<AsyncAutoResetEvent> ResetEvents { get; set; } = new List<AsyncAutoResetEvent>();

        public void AddLine(string output)
        {
            lock (_lock)
            {
                _content += output + Environment.NewLine;

                foreach (var resetEvent in this.ResetEvents)
                {
                    resetEvent.Set();
                }
            }
        }

        public void SetCompleted()
        {
            lock (_lock)
            {
                _complete = true;

                foreach (var resetEvent in this.ResetEvents)
                {
                    resetEvent.Set();
                }
            }
        }

        public AsyncAutoResetEvent Subscribe()
        {
            lock (_lock)
            {
                var resetEvent = new AsyncAutoResetEvent(false);
                this.ResetEvents.Add(resetEvent);
                return resetEvent;
            }
        }

        public void Unsubscribe(AsyncAutoResetEvent resetEvent)
        {
            lock (_lock)
            {
                this.ResetEvents.Remove(resetEvent);
            }
        }
    }
}
