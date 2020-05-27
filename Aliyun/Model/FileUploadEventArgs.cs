using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TransferLib.Aliyun.Model
{
    public class FileUploadEventArgs : EventArgs
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; }

        public FileUploadEventArgs(string parameter, bool success = true)
        {
            Success = success;
            ErrorMessage = parameter;
        }
    }
}
