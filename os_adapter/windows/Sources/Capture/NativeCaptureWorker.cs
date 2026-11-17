// Copyright (c) Huawei Technologies Co., Ltd. 2026. All rights reserved.

using System;
using System.Collections.Generic;

namespace ComputerUse.Windows
{
    // Own only reusable process/device resources. A reply is published after
    // its request-local capture session and frame pool have been disposed.
    internal sealed class NativeCaptureWorker : IDisposable
    {
        readonly object gate = new object();
        readonly Func<INativeWorkerProcess> create;
        INativeWorkerProcess worker;
        long sequence;
        bool retired, disposed;

        internal NativeCaptureWorker(Func<INativeWorkerProcess> create) { this.create = create; }

        void Discard()
        {
            var previous = worker;
            worker = null;
            if (previous == null)
                return;
            bool confirmed = false;
            try { confirmed = previous.Stop(); }
            finally
            {
                try { previous.Dispose(); }
                finally { retired |= !(confirmed || previous.ExitConfirmed); }
            }
        }

        internal Dictionary<string, object> Exchange(Dictionary<string, object> parameters, Request request, int budgetMs)
        {
            lock (gate)
            {
                request.Check();
                if (disposed || retired)
                    throw new Failure("observation_unavailable", "capture helper cleanup is unconfirmed or its owner is closed");
                try
                {
                    double deadline = Math.Min(request.Deadline, Wire.Now + budgetMs);
                    if (worker == null)
                        worker = create();
                    var message = new Dictionary<string, object>(parameters);
                    long id = ++sequence;
                    message["id"] = id;
                    worker.Send(message);
                    string line = null;
                    while (line == null && Wire.Now < deadline)
                    {
                        request.Check();
                        line = worker.Poll((int)Math.Ceiling(Math.Min(20, Math.Max(0, deadline - Wire.Now))));
                    }
                    request.Check();
                    if (line == null)
                        throw new Failure("observation_unavailable", "capture timed out; helper terminated");
                    var result = Wire.Object(Wire.Serializer().DeserializeObject(line));
                    if (!result.ContainsKey("id") || Convert.ToInt64(result["id"]) != id)
                        throw new Failure("observation_unavailable", "capture helper response identity mismatch");
                    if (result.ContainsKey("error"))
                        throw new Failure("observation_unavailable", (string)result["error"]);
                    return result;
                }
                catch
                {
                    Discard();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed)
                    return;
                disposed = true;
                Discard();
                if (retired)
                    throw new Failure("observation_unavailable", "capture helper cleanup could not be confirmed");
            }
        }
    }
}
