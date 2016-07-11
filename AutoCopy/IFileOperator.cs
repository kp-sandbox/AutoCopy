using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoCopy {
  interface IFileOperator {
    Task CopyFile(string srcFileRelPath, string destFileRelPath);
    Task CopyFolder(string srcFolderRelPath, string destFolderRelPath);

    Task MoveFile(string oldFileRelPath, string newFileRelPath);
    Task MoveFolder(string oldFolderRelPath, string newFolderRelPath);

    Task DeleteFile(string deleteFileRelPath);
    Task DeleteFolder(string deleteFolderRelPath);
  }
}
