using System;

namespace TransferLib
{
    public class FileDownLoadCompletedEventArgs : EventArgs
    {
        public TransferState State { get; private set; }
        public string ErrorMessage { get; private set; }
        public string TargetFile { get; private set; }
        public object UserState { get; private set; }

        public FileDownLoadCompletedEventArgs(TransferState state, string parameter)
        {
            State = state;
            switch (State)
            {
                case TransferState.Completed:
                    TargetFile = parameter;
                    break;
                case TransferState.Error:
                    ErrorMessage = parameter;
                    break;
            }
        }

        public FileDownLoadCompletedEventArgs(TransferState state, string parameter, object userState) : this(state, parameter)
        {
            UserState = userState;
        }
    }
}