using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
using System.Threading;
using System.Diagnostics;
using System.Text;
using Microsoft.VisualBasic.CompilerServices;
using Microsoft.VisualBasic.Devices;
using System.Text.RegularExpressions;

namespace AutoCopy {
  static class StartHere {
    private static Mutex mutex = new Mutex(true, "{A2956D8B-B40E-412F-968B-4B4FC00C93EB}");
    [STAThread]
    static void Main() {
      if (mutex.WaitOne(TimeSpan.Zero, true)) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new AutoCopyApp());
        mutex.ReleaseMutex();
      } else {
        MessageBox.Show("Only one instance at a time.");
      }
    }
  }

  static class Helper {
    public static string GetAttributeValue(this XElement element, string key) {
      return element.Attribute(key) == null ? null : element.Attribute(key).Value;
    }

  }

  public class AutoCopyApp : Form {
    private readonly string m_configXmlPath;
    private readonly string m_logFilePath;
    private readonly Logger m_logger = null;
    private NotifyIcon trayIcon;
    enum WatchMode { File, Directory };

    //private List<Tuple<DirectoryInfo, string, WatchMode, string>> watchlist = null;
    private List<FileSystemWatcher> watchers = null;

    public AutoCopyApp() : this(new string[] { }) {
    }

    public AutoCopyApp(string[] args) {
      if (args.Length > 0) {
        m_configXmlPath = args[0];
      } else {
        m_configXmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "configuration.xml");
      }
      m_logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, String.Format("autocopy-{0}.log", DateTime.Now.ToString("yyyy-MM-dd")));
      m_logger = Logger.GetLogger(m_logFilePath);
      try {
        LoadWatchers();
        trayIcon = new NotifyIcon();
        trayIcon.Text = "AutoCopy";
        trayIcon.Icon = AutoCopyResx.TrayIcon;
        trayIcon.ContextMenu = new ContextMenu(new MenuItem[] {
          new MenuItem("Open Configuration XML", (s, e) => {
            Process.Start("notepad.exe", m_configXmlPath);
          }),
          new MenuItem("Reload Configuration", (s, e) => {
            m_logger.WriteLog("Reload configuration.");
            LoadWatchers();
            trayIcon.ContextMenu.MenuItems[2].Text = String.Format("{0} path(s) is watching", watchers.Count());
          }),
          new MenuItem(String.Format("{0} path(s) is watching", watchers.Count())),
          new MenuItem("Exit", (s, e) => {
            m_logger.WriteLog("Application exit.");
            Application.Exit();
          })
        });
        trayIcon.ContextMenu.MenuItems[2].Text = String.Format("{0} path(s) is watching", watchers.Count());
        trayIcon.Visible = true;
        m_logger.WriteLog("Application started successfully.");
      } catch (Exception ex) {
        MessageBox.Show(ex.Message);
        Application.Exit();
      }
    }

    protected int LoadWatchers() {
      try {
        if (File.Exists(m_configXmlPath)) {
          XDocument doc = XDocument.Load(m_configXmlPath);
          IEnumerable<XElement> nodes = doc.Elements("autocopy").Descendants("copynode");
          //watchlist = new List<Tuple<DirectoryInfo, string, WatchMode, string>>();
          watchers = new List<FileSystemWatcher>();
          foreach (XElement node in nodes) {
            string source = node.GetAttributeValue("source");
            string destination = node.GetAttributeValue("destination");
            string filter = node.GetAttributeValue("exclude") ?? "*.*";
            string privateKeyPath = node.GetAttributeValue("sshAuthKey") ?? String.Empty;

            if (!String.IsNullOrEmpty(source) && !String.IsNullOrEmpty(destination)) {
              try {
                if (IsDirectoryPath(source)) {
                  // watch directory
                  watchers.Add(InitWatcher(new DirectoryInfo(source), destination, filter, WatchMode.Directory, privateKeyPath));
                } else {
                  // watch file
                  watchers.Add(InitWatcher((new FileInfo(source)).Directory, destination, Path.GetFileName(source), WatchMode.File, privateKeyPath));
                }
              } catch (IOException ex) {
                m_logger.WriteLog(ex.ToString(), Logger.LogLevel.Error);
              }
            }
          }
        } else {
          throw new ArgumentException(String.Format("Configuration file not found: {0}", m_configXmlPath));
        }
      } catch (Exception ex) {
        m_logger.WriteLog(ex.ToString(), Logger.LogLevel.Error);
        throw;
      }
      return watchers.Count();
    }

    private bool MatchFilter(string filePath, string filter) {
      string[] filters = filter.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
      foreach (string include in filters) {
        if (LikeOperator.LikeString(filePath, include, Microsoft.VisualBasic.CompareMethod.Text)) {
          return false;
        }
      }
      return true;
    }

    private FileSystemWatcher InitWatcher(DirectoryInfo sourceDirectory, string targetPath, string filter, WatchMode watchMode, string privateKey = null) {

      IFileOperator fileOperator = GetFileOperator(sourceDirectory.FullName, targetPath, privateKey);

      FileSystemWatcher watcher = new FileSystemWatcher() {
        Path = sourceDirectory.FullName,
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size
      };

      if (watchMode == WatchMode.File) {
        watcher.Filter = filter;
      }

      watcher.Changed += async (s, e) => {
        try {
          string source = e.FullPath;
          if (MatchFilter(source, filter)) {
            if (File.Exists(source)) {
              await fileOperator.CopyFile(e.Name, e.Name);
              m_logger.WriteLog(string.Format("{0}: {1}", (object)e.ChangeType, (object)e.Name));
            }
          }
        } catch (Exception ex) {
          m_logger.WriteLog(ex.ToString(), Logger.LogLevel.Error);
        }
      };
      watcher.Created += async (s, e) => {
        try {
          string source = e.FullPath;
          if (MatchFilter(source, filter)) {
            if (File.Exists(source)) {
              await fileOperator.CopyFile(e.Name, e.Name);
              m_logger.WriteLog(string.Format("{0}: {1}", (object)e.ChangeType, (object)e.Name));
            } else if (Directory.Exists(source)) {
              await fileOperator.CopyFolder(e.Name, e.Name);
              m_logger.WriteLog(string.Format("{0}: {1}", (object)e.ChangeType, (object)e.Name));
            }
          }
        } catch (Exception ex) {
          m_logger.WriteLog(ex.ToString(), Logger.LogLevel.Error);
        }
      };
      watcher.Deleted += async (s, e) => {
        try {
          string path = Path.Combine(sourceDirectory.FullName, e.Name);
          Thread.Sleep(100);
          if (IsDirectoryPath(path)) {
            await fileOperator.DeleteFolder(e.Name);
            m_logger.WriteLog(string.Format("{0}: {1}", (object)e.ChangeType, (object)e.Name));
          } else {
            await fileOperator.DeleteFile(e.Name);
            m_logger.WriteLog(string.Format("{0}: {1}", (object)e.ChangeType, (object)e.Name));
          }
        } catch (Exception ex) {
          m_logger.WriteLog(ex.ToString(), Logger.LogLevel.Error);
        }
      };
      watcher.Renamed += async (s, e) => {
        try {
          string sourceNew = e.FullPath;
          if (File.Exists(sourceNew)) {
            await fileOperator.MoveFile(e.OldName, e.Name);
          } else if (Directory.Exists(sourceNew)) {
            await fileOperator.MoveFolder(e.OldName, e.Name);
          }

          //if (MatchFilter(sourceOld, filter)) {
          //  if (!Directory.Exists(Path.GetDirectoryName(destination)))
          //    Directory.CreateDirectory(Path.GetDirectoryName(destination));
          //  if (File.Exists(sourceOld)) {
          //    await this.WaitUntilUnlocked(new FileInfo(sourceOld));
          //    File.Move(sourceOld, destination);
          //    WriteLog(string.Format("{0}: {1}", (object)e.ChangeType, (object)e.Name));
          //  } else if (Directory.Exists(sourceOld)) {
          //    Directory.Move(sourceOld, destination);
          //    WriteLog(string.Format("{0}: {1}", (object)e.ChangeType, (object)e.Name));
          //  } else if (File.Exists(sourceNew)) {
          //    await this.WaitUntilUnlocked(new FileInfo(sourceNew));
          //    File.Copy(sourceNew, destination, true);
          //    WriteLog(string.Format("{0}: {1}", (object)e.ChangeType, (object)e.Name));
          //  } else if (!Directory.Exists(sourceNew)) {
          //  }
          //}

        } catch (Exception ex) {
          m_logger.WriteLog(ex.ToString(), Logger.LogLevel.Error);
        }
      };
      watcher.EnableRaisingEvents = true;
      watchers.Add(watcher);
      m_logger.WriteLog(String.Format("Added watch path: {0} -> {1}, {2}: {3}", sourceDirectory, targetPath,
        (watchMode == WatchMode.File ? "filename" : "exclude"),
        (watchMode == WatchMode.File ? watcher.Filter : filter)
      ));
      return watcher;
    }

    private bool IsDirectoryPath(string path) {
      return Path.GetDirectoryName(path).Equals(path.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private async Task WaitUntilUnlocked(FileInfo file) {
      int retry = 0;
      while (++retry < 20) {
        FileStream stream = (FileStream)null;
        try {
          stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
          break;
        } catch (IOException) {
        } finally {
          if (stream != null) {
            stream.Close();
          }
        }
        await Task.Run((Action)(() => Thread.Sleep(500)));
      }
    }

    protected override void OnLoad(EventArgs e) {
      Visible = false;
      ShowInTaskbar = false;
      base.OnLoad(e);
    }

    protected override void Dispose(bool isDisposing) {
      if (isDisposing) {
        trayIcon.Dispose();
      }
      base.Dispose(isDisposing);
    }

    //private void WriteError(string text) {
    //  WriteLog(text, "ERROR");
    //}

    //private void WriteLog(string text, string logType = "INFO") {
    //  int retry = 0;
    //  while (++retry < 20) {
    //    try {
    //      using (StreamWriter writer = File.AppendText(m_logFilePath)) {
    //        writer.WriteLine(String.Format("{0} - [{1}] - {2}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), logType, text));
    //      }
    //      return;
    //    } catch (IOException) {
    //      Thread.Sleep(500);
    //    }
    //  }
    //}

    private bool ValidWindowsDiectory(string sourcePath) {
      try {
        return (Directory.Exists(Path.GetDirectoryName(sourcePath)));
      } catch (IOException) { return false; }
    }

    private IFileOperator GetFileOperator(string sourcePath, string targetPath, string privateKey = null) {
      if (ValidWindowsDiectory(targetPath)) {
        return new WindowsFileOperator(sourcePath, targetPath, m_logger);
      }
      if (targetPath.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase)) {
        return new SftpFileOperator(sourcePath, targetPath, privateKey, m_logger);
      }
      throw new Exception(String.Format("Unable to determine target file system, {0}", targetPath));
    }

  }

}
