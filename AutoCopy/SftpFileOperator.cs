using Renci.SshNet;
using Renci.SshNet.Async;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCopy {

  class SftpFileOperator : BaseFileOperator<SftpClient>, IFileOperator {
    private readonly ConnectionInfo m_connInfo;

    private static string ParseBaseFolder(string destBaseFolder) {
      Match m = Regex.Match(destBaseFolder, @"^sftp://(?<username>.*?)@(?<hostname>.*?)/(?<basePath>.*?)$");
      return m.Groups["basePath"].Value;
    }

    public SftpFileOperator(string srcBaseFolder, string destBaseFolder, string privateKeyFile, Logger logger)
      : base(srcBaseFolder, ParseBaseFolder(destBaseFolder), logger) {
      Match m = Regex.Match(destBaseFolder, @"^sftp://(?<username>.*?)@(?<hostname>.*?)/(?<basePath>.*?)$");
      this.m_connInfo = new ConnectionInfo(
        m.Groups["hostname"].Value,
        m.Groups["username"].Value,
        new PrivateKeyAuthenticationMethod(m.Groups["username"].Value, new PrivateKeyFile(privateKeyFile))
       );
    }

    protected override SftpClient CreateClient() {
      SftpClient client = new SftpClient(this.m_connInfo);
      if (!client.IsConnected) {
        client.Connect();
        try {
          client.ChangeDirectory(this.m_destBaseFolder);
        } catch (SftpPathNotFoundException) {
          throw new ArgumentException(String.Format("Target basepath in Sftp not found: {0}", this.m_destBaseFolder));
        }
      }
      client.ChangeDirectory(this.m_destBaseFolder);
      return client;
    }

    protected override void DisposeClient(SftpClient client) {
      client.Disconnect();
      client.Dispose();
    }

    public Task CopyFile(string srcFileRelPath, string destFileRelPath) {
      Task ensureDirectory = EnsureDirectory(destFileRelPath);
      return AddTask((client) => {
        Task.WaitAll(ensureDirectory);
        return RetryTask(() => {
          string sourcePath = Path.Combine(this.m_srcBaseFolder, srcFileRelPath);
          string targetPath = CombineLinuxPath(this.m_destBaseFolder, destFileRelPath);
          using (FileStream fs = new FileStream(sourcePath, FileMode.Open)) {
            if (fs != null) {
              client.UploadFile(fs, targetPath, true);
              m_logger.WriteLog(String.Format("Copied file '{0}' -> '{1}'", sourcePath, targetPath));
            }
          }
          return Task.Delay(0);
        });
      });
    }

    public Task CopyFolder(string srcFolderRelPath, string destFolderRelPath) {
      List<Task> tasks = new List<Task>();
      foreach (FileInfo fi in new DirectoryInfo(Path.Combine(base.m_srcBaseFolder, srcFolderRelPath)).GetFiles("*", SearchOption.AllDirectories)) {
        string relativePath = fi.FullName.Substring(base.m_srcBaseFolder.Length);
        tasks.Add(CopyFile(relativePath, relativePath));
      }
      return Task.WhenAll(tasks);
    }

    public Task DeleteFile(string deleteFileRelPath) {
      return AddTask((client) => {
        string target = CombineLinuxPath(base.m_destBaseFolder, deleteFileRelPath);
        EmptyDirectory(target);
        client.Delete(target);
        m_logger.WriteLog(String.Format("Deleted path '{0}'", target));
        return Task.Delay(0);
      });
    }

    public Task DeleteFolder(string deleteFolderRelPath) {
      return AddTask((client) => {
        string target = CombineLinuxPath(base.m_destBaseFolder, deleteFolderRelPath);
        EmptyDirectory(target);
        client.Delete(target);
        m_logger.WriteLog(String.Format("Deleted path '{0}'", target));
        return Task.Delay(0);
      });
    }
    
    protected void EmptyDirectory(string targetDirectory) {
      using (SshClient ssh = new SshClient(this.m_connInfo)) {
        ssh.Connect();
        ssh.RunCommand(String.Format("cd \"{0}\"; rm -r *;", targetDirectory));
        ssh.Disconnect();
      }
    }

    public Task MoveFile(string oldFileRelPath, string newFileRelPath) {
      return AddTask((client) => {
        string source = CombineLinuxPath(base.m_destBaseFolder, oldFileRelPath);
        string target = CombineLinuxPath(base.m_destBaseFolder, newFileRelPath);
        client.RenameFile(source, target);
        m_logger.WriteLog(String.Format("Moved file '{0}' -> '{1}'", source, target));
        return Task.Delay(0);
      });
    }

    public Task MoveFolder(string oldFolderRelPath, string newFolderRelPath) {
      return AddTask((client) => {
        string source = CombineLinuxPath(base.m_destBaseFolder, oldFolderRelPath);
        string target = CombineLinuxPath(base.m_destBaseFolder, newFolderRelPath);
        client.RenameFile(source, target);
        m_logger.WriteLog(String.Format("Moved directory '{0}' -> '{1}'", source, target));
        return Task.Delay(0);
      });
    }

    private string CombineLinuxPath(params string[] paths) {
      return Path.Combine(paths).Replace('\\', '/');
    }

    private string[] SplitDirectories(string path) {
      return Path.GetDirectoryName(path).Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private Task EnsureDirectory(string path) {
      return AddTask((client) => {
        string[] directories = SplitDirectories(path);
        client.ChangeDirectory(base.m_destBaseFolder);
        foreach (string d in directories) {
          if (!client.Exists(d)) {
            client.CreateDirectory(d);
          }
          client.ChangeDirectory(CombineLinuxPath(client.WorkingDirectory, d));
        }
        m_logger.WriteLog(String.Format("Directory ensured '{0}'", client.WorkingDirectory));
        return Task.Delay(0);
      });
    }

  }
}
