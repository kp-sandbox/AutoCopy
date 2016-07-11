using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCopy {
  public class Logger {

    public enum LogLevel { Debug, Info, Error };
    private static Logger _instance = null;
    private readonly string m_logFilePath = null;
    private readonly BlockingCollection<Tuple<LogLevel, DateTime, string>> m_queue = new BlockingCollection<Tuple<LogLevel, DateTime, string>>();
    private readonly Thread m_worker = null;

    public static Logger GetLogger(string logFilePath) {
      if (_instance == null) {
        _instance = new Logger(logFilePath);
      }
      return _instance;
    }

    private Logger(string logFilePath) {
      m_logFilePath = logFilePath;
      if (!Directory.Exists(Path.GetDirectoryName(logFilePath))) {
        Directory.CreateDirectory(Path.GetDirectoryName(logFilePath));
      }
      m_worker = new Thread(OutputLog);
      m_worker.Start();
    }

    private void OutputLog() {
      Tuple<LogLevel, DateTime, string> m;
      while (m_queue.TryTake(out m, 10000)) {
        using (StreamWriter writer = File.AppendText(m_logFilePath)) {
          writer.WriteLine(String.Format("{0} - [{1}] - {2}", m.Item2.ToString("yyyy-MM-dd HH:mm:ss"), m.Item1, m.Item3));
        }
      }
      OutputLog();
      //var m = m_queue.Take();
      //using (StreamWriter writer = File.AppendText(m_logFilePath)) {
      //  writer.WriteLine(String.Format("{0} - [{1}] - {2}", m.Item2.ToString("yyyy-MM-dd HH:mm:ss"), m.Item1, m.Item3));
      //}
    }

    public void WriteLog(string message, LogLevel level = LogLevel.Info) {
      m_queue.Add(new Tuple<LogLevel, DateTime, string>(level, DateTime.Now, message));
    }


  }
}
