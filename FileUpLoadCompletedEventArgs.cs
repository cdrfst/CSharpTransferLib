using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TransferLib
{
    public class FileUpLoadCompletedEventArgs : EventArgs
    {
        public TransferState State { get; private set; }
        public string ErrorMessage { get; private set; }

        public FileUpLoadCompletedEventArgs(TransferState state, string parameter)
        {
            State = state;
            ErrorMessage = parameter;
        }
    }
}
