using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using TransferLib.Extensions;

namespace TransferLib.Ftp
{
    public class FtpFileUploader
    {
        #region 私有字段

        protected int TimeOut;
        private readonly string _userName;
        private readonly string _password;
        private int _fileUploadedOldSize;
        private System.Threading.Timer _progressTimer;
        /// <summary>
        /// 是否被动连接(服务器)
        /// </summary>
        private readonly bool _usePassive;
        private readonly string _uploadFileUrl;
        private readonly string _localFile;
        private Exception _exception;
        private int _fileUploadedSize;

        #endregion

        public int TotalFileSize { get; private set; }
        public TransferState CurrentState { get; private set; }


        public delegate void FtpFileUploadCompletedEventHandle(FtpFileUploader sender, FileUpLoadCompletedEventArgs e);
        public delegate void FileTransferProgressChangedEventHandler(object sender, TransferProgressChangedEventArgs e);

        public event FtpFileUploadCompletedEventHandle FtpFileUploadCompleted;
        public event FileTransferProgressChangedEventHandler ProgressChanged;

        public FtpFileUploader(string localFile, string uploadFileAddress, string userName, string password, int timeOut, bool usePassive = true)
        {
            _localFile = localFile;
            _uploadFileUrl = uploadFileAddress;
            _userName = userName;
            _password = password;
            _usePassive = usePassive;
            TimeOut = timeOut;
            GetFileSize();
        }

        private void GetFileSize()
        {
            try
            {
                var ftpReq1 = (FtpWebRequest)WebRequest.Create(_uploadFileUrl);
                ftpReq1.Credentials = new NetworkCredential(_userName, _password);
                ftpReq1.Method = WebRequestMethods.Ftp.GetFileSize;
                ftpReq1.KeepAlive = false;
                ftpReq1.Timeout = TimeOut;
                ftpReq1.ReadWriteTimeout = TimeOut;
                ftpReq1.UseBinary = true;
                ftpReq1.UsePassive = _usePassive;
                using (var ftpResponse = (FtpWebResponse)ftpReq1.GetResponse())
                {
                    _fileUploadedSize = (int)ftpResponse.ContentLength;
                }
            }
            catch
            {
                //ignore;上传文件(服务器不存在该文件时)获取FileSize报550错误!
            }
        }

        public void UploadFileAsync()
        {
            var act = new Action(UploadFile);
            act.BeginInvoke(callback =>
            {
                if (callback.IsCompleted)
                {
                    _progressTimer.Dispose();
                    OnProgressChanged(_fileUploadedSize, string.Empty);
                    OnFtpFileUploadCompleted(new FileUpLoadCompletedEventArgs(_exception == null ? TransferState.Completed : TransferState.Error, _exception == null ? string.Empty : _exception.GetErrorString()));
                }
            }, null);
        }

        private void UploadFile()
        {
            try
            {
                CurrentState = TransferState.Uploading;
                var ftpReq = (FtpWebRequest)WebRequest.Create(_uploadFileUrl);
                ftpReq.Method = WebRequestMethods.Ftp.UploadFile;
                ftpReq.Timeout = TimeOut;
                ftpReq.Credentials = new NetworkCredential(_userName, _password);
                _progressTimer = new System.Threading.Timer(callback =>
                {
                    var speed = (_fileUploadedSize - _fileUploadedOldSize) / 1024F;
                    var unit = "K";
                    var bigThen1K = speed > 1024;
                    if (bigThen1K)
                    {
                        speed = speed / 1024;
                        unit = "M";
                    }
                    OnProgressChanged(_fileUploadedSize, string.Format("{0}{1}/s", Math.Round(speed, 1), unit));
                    _fileUploadedOldSize = _fileUploadedSize;
                }, null, 1000, 1000);
                using (var fs = File.OpenRead(_localFile))
                {
                    TotalFileSize = (int)fs.Length;
                    fs.Seek(_fileUploadedSize, SeekOrigin.Begin);
                    ftpReq.ContentOffset = _fileUploadedSize;
                    ftpReq.KeepAlive = false;
                    ftpReq.ReadWriteTimeout = TimeOut;
                    ftpReq.UseBinary = true;
                    ftpReq.UsePassive = _usePassive;
                    using (var reqStream = ftpReq.GetRequestStream())
                    {
                        var buffer = new byte[1024];
                        var readOffset = fs.Read(buffer, 0, buffer.Length);
                        while (readOffset > 0)
                        {
                            reqStream.Write(buffer, 0, readOffset);
                            readOffset = fs.Read(buffer, 0, buffer.Length);
                            _fileUploadedSize += readOffset;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _exception = e;
            }
        }

        protected virtual void OnFtpFileUploadCompleted(FileUpLoadCompletedEventArgs e)
        {
            if (FtpFileUploadCompleted != null) FtpFileUploadCompleted(this, e);
        }

        protected virtual void OnProgressChanged(int progressPercentage, string speed)
        {
            if (ProgressChanged != null)
            {
                ProgressChanged(this, new TransferProgressChangedEventArgs(progressPercentage, speed));
            }
        }
    }
}
