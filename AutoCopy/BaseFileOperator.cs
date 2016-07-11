using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCopy {

  public abstract class BaseFileOperator<TClient> : IDisposable {
    const int RETRY_MAX = 20;
    const int THREAD_SLEEP = 500;

    protected readonly string m_destBaseFolder;
    protected readonly string m_srcBaseFolder;
    protected readonly Logger m_logger;

    private readonly BlockingCollection<Tuple<Func<TClient, Task>, FileOperation>> waitingTasks = new BlockingCollection<Tuple<Func<TClient, Task>, FileOperation>>();
    private readonly List<TClient> clients = new List<TClient>();
    private readonly List<Task> tasks = new List<Task>();

    private class FileOperation {
      public readonly AutoResetEvent AutoResetEvent = new AutoResetEvent(false);
      public Task Task;
    }

    protected BaseFileOperator(string srcBaseFolder, string destBaseFolder, Logger logger) {
      this.m_srcBaseFolder = srcBaseFolder;
      this.m_destBaseFolder = destBaseFolder;
      this.m_logger = logger;
    }

    public void Dispose() {
      waitingTasks.CompleteAdding();
      Task.WaitAll(tasks.ToArray());
      foreach (TClient client in this.clients) {
        DisposeClient(client);
      }
    }

    protected abstract TClient CreateClient();

    protected abstract void DisposeClient(TClient client);

    protected void ExecuteNextTask(TClient client) {
      Tuple<Func<TClient, Task>, FileOperation> m;
      while (true) {
        while (waitingTasks.TryTake(out m, 10000)) {
          Task t = m.Item1(client);
          Task.WaitAll(t);
          m.Item2.Task = t;
          m.Item2.AutoResetEvent.Set();
        }
        if (waitingTasks.IsAddingCompleted) {
          return;
        }
      }
    }

    protected Task AddTask(Func<TClient, Task> func) {
      if (tasks.Count == 0) {
        foreach (int i in Enumerable.Range(0, 2)) {
          TClient client = CreateClient();
          tasks.Add(Task.Run(() => ExecuteNextTask(client)));
          this.clients.Add(client);
        }
      }
      if (waitingTasks.IsAddingCompleted) {
        throw new InvalidOperationException("");
      }
      FileOperation item = new FileOperation();
      waitingTasks.Add(Tuple.Create(func, item));
      return Task.Run(() => {
        item.AutoResetEvent.WaitOne();
        if (item.Task.Exception != null) {
          ExceptionDispatchInfo.Capture(item.Task.Exception).Throw();
          throw item.Task.Exception;
        }
      });
    }

    protected Task RetryTask(Func<Task> action) {
      int retry = 0;
      Func<Task, Task> m = null;
      m = t => {
        if (t.IsCompleted) {
          return Task.WhenAll(new Task[0]);
        }
        if (++retry > RETRY_MAX) {
          ExceptionDispatchInfo.Capture(t.Exception).Throw();
          throw t.Exception;
        }
        if (t.Exception.InnerException is IOException) {
          return Task.Delay(THREAD_SLEEP).ContinueWith(s => action()).ContinueWith(m);
        } else {
          ExceptionDispatchInfo.Capture(t.Exception).Throw();
          throw t.Exception;
        }
      };
      return Task.Run(action).ContinueWith(m);
    }
  }
}
