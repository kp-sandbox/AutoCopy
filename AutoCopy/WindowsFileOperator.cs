using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoCopy {
  class WindowsFileOperator : BaseFileOperator<WindowsFileOperator.DummyClient>, IFileOperator {
    public class DummyClient { }

    public WindowsFileOperator(string srcBaseFolder, string destBaseFolder, Logger logger)
      : base(srcBaseFolder, destBaseFolder, logger) {
    }

    protected override DummyClient CreateClient() {
      return new DummyClient();
    }

    protected override void DisposeClient(DummyClient client) {

    }

    public Task DeleteFile(string deleteFileRelPath) {
      string filePath = Path.Combine(m_destBaseFolder, deleteFileRelPath);
      if (File.Exists(filePath)) {
        return RetryTask(() => {
          File.Delete(filePath);
          return Task.WhenAll(new Task[0]);
        });
      }
      return Task.Delay(0);
    }

    public Task DeleteFolder(string deleteFolderRelPath) {
      string folderPath = Path.Combine(m_destBaseFolder, deleteFolderRelPath);
      if (Directory.Exists(folderPath)) {
        return RetryTask(() => {
          Directory.Delete(folderPath, true);
          return Task.WhenAll(new Task[0]);
        });
      }
      return Task.Delay(0);
    }

    public Task MoveFile(string oldFileRelPath, string newFileRelPath) {
      string oFilePath = Path.Combine(m_destBaseFolder, oldFileRelPath);
      string nFilePath = Path.Combine(m_destBaseFolder, newFileRelPath);

      if (File.Exists(oFilePath)) {
        return RetryTask(() => {
          File.Move(oFilePath, nFilePath);
          return Task.WhenAll(new Task[0]);
        });
      }
      return Task.Delay(0);
    }

    public Task MoveFolder(string oldFolderRelPath, string newFolderRelPath) {
      string oFolderPath = Path.Combine(m_destBaseFolder, oldFolderRelPath);
      string nFolderPath = Path.Combine(m_destBaseFolder, newFolderRelPath);

      if (Directory.Exists(oFolderPath)) {
        return RetryTask(() => {
          Directory.Move(oFolderPath, nFolderPath);
          return Task.WhenAll(new Task[0]);
        });
      }
      return Task.Delay(0);
    }

    public Task CopyFile(string srcFileRelPath, string destFileRelPath) {
      string srcFilePath = Path.Combine(m_srcBaseFolder, srcFileRelPath);
      string destFilePath = Path.Combine(m_destBaseFolder, destFileRelPath);

      if (File.Exists(srcFilePath)) {
        if (!Directory.Exists(Path.GetDirectoryName(destFilePath))) {
          Directory.CreateDirectory(Path.GetDirectoryName(destFilePath));
        }
        return RetryTask(() => {
          File.Copy(srcFilePath, destFilePath, true);
          return Task.WhenAll(new Task[0]);
        });
      }
      return Task.Delay(0);
    }

    public Task CopyFolder(string srcFolderRelPath, string destFolderRelPath) {
      string srcFolderPath = Path.Combine(m_srcBaseFolder, srcFolderRelPath);
      string destFolderPath = Path.Combine(m_destBaseFolder, destFolderRelPath);

      if (Directory.Exists(srcFolderPath)) {
        return RetryTask(() => {
          (new Computer()).FileSystem.CopyDirectory(srcFolderPath, destFolderPath, true);
          return Task.WhenAll(new Task[0]);
        });
      }
      return Task.Delay(0);
    }

  }
}
