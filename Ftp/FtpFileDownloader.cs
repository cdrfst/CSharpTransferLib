using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TransferLib.Extensions;

namespace TransferLib.Ftp
{
    public sealed class FtpFileDownloader : FileDownloader
    {
        #region 私有字段

        private readonly string _userName;
        private readonly string _password;
        /// <summary>
        /// 是否被动连接(服务器)
        /// </summary>
        private readonly bool _usePassive;

        #endregion

        #region 构造函数

        /// <summary>
        /// 创建Ftp文件下载实例
        /// (异常:System.Exception)
        /// </summary>
        /// <param name="downloadFileUrl"></param>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        /// <param name="usePassive">FTP服务器是否被动连接;如客户端有防火墙设置为true</param>
        public FtpFileDownloader(string downloadFileUrl, string userName, string password, bool usePassive)
        {
            DownloadFileUrl = downloadFileUrl;
            _userName = userName;
            _password = password;
            _usePassive = usePassive;

            TempDir = Path.Combine(TempDir, "uclass", "Ftp");
            Segments = new Lazy<List<FileSegment>>();
            SegmentRunningTasks = new Lazy<List<Task>>();
            var fileInfo = GetFileInfo(downloadFileUrl);
            FileName = fileInfo.Item1;
            ExtensionName = fileInfo.Item2;
            GetFileSize();
            GetLastModified();
        }


        /// <summary>
        /// 创建Ftp文件下载实例
        /// (异常:System.Exception)
        /// </summary>
        /// <param name="downloadFileUrl"></param>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        /// <param name="usePassive">FTP服务器是否被动连接;如客户端有防火墙设置为true</param>
        /// <param name="maxThreadCount">用于下载单个文件的线程数</param>
        public FtpFileDownloader(string downloadFileUrl, string userName, string password, bool usePassive, int maxThreadCount = 1)
            : this(downloadFileUrl, userName, password, usePassive)
        {
            base.MaxThreadCount = maxThreadCount;
        }

        #endregion

        #region 重写父类方法
        public override void DownLoadFileAsync(object userState = null)
        {
            UserState = userState;
            var action = new Action(() =>
            {
                //获取拆分的下载片断
                FileSegmentsManager = FtpFileSegmentsManager.Create();
                FileSegmentsManager.AddFile(TempDir, FileName, FileLength, LastModified);
                Segments.Value.AddRange(FileSegmentsManager.GetSegmentsByFileName(FileName));
                var sgmtGroupList = Segments.Value.Take(MaxThreadCount);
                int skipCount = 0;
                while (sgmtGroupList.Any())
                {
                    foreach (var segment in sgmtGroupList)
                    {
                        var cts = new CancellationTokenSource();
                        var task = Task.Factory.StartNew(FillSenmentData, segment, cts.Token);
                        SegmentRunningTasks.Value.Add(task);
                        //task.Wait(); //方便调试
                    };
                    try
                    {
                        Task.WaitAll(SegmentRunningTasks.Value.ToArray());
                    }
                    catch (AggregateException ae)
                    {
                        OnFileDownloadCompletedEventHandler(new FileDownLoadCompletedEventArgs(TransferState.Error, ae.GetErrorString(), UserState));
                        break;
                    }
                    catch (Exception e)
                    {
                        OnFileDownloadCompletedEventHandler(new FileDownLoadCompletedEventArgs(TransferState.Error, e.GetErrorString(), UserState));
                        break;
                    }
                    skipCount += MaxThreadCount;
                    sgmtGroupList = Segments.Value.Skip(skipCount).Take(MaxThreadCount);
                }
            });
            action.BeginInvoke(null, null);
        }

        public override void Dispose()
        {
            if (FileSegmentsManager != null)
                FileSegmentsManager.RemoveFile(FileName);
            base.Dispose();
        }

        #endregion

        #region 私有方法

        private void GetFileSize()
        {
            try
            {
                var hwrq = (FtpWebRequest)WebRequest.Create(DownloadFileUrl);
                hwrq.Credentials = new NetworkCredential(_userName, _password);
                hwrq.KeepAlive = false;
                hwrq.Method = WebRequestMethods.Ftp.GetFileSize;
                hwrq.Timeout = TimeOut;
                hwrq.UseBinary = true;
                hwrq.UsePassive = _usePassive;
                using (var response = (FtpWebResponse)hwrq.GetResponse())
                {
                    FileLength = (int)response.ContentLength;
                }
            }
            catch (WebException we)
            {
                throw new WebException(string.Format("FtpGetFileSize:\r\n{0}", we.GetErrorString()));
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("Ftp获取文件大小时异常:\r\n{0}", e.GetErrorString()));
            }
        }


        private void GetLastModified()
        {
            try
            {
                var hwrq = (FtpWebRequest)WebRequest.Create(DownloadFileUrl);
                hwrq.Credentials = new NetworkCredential(_userName, _password);
                hwrq.KeepAlive = false;
                hwrq.Method = WebRequestMethods.Ftp.GetDateTimestamp;
                hwrq.Timeout = TimeOut;
                hwrq.UseBinary = true;
                hwrq.UsePassive = _usePassive;

                using (var response = (FtpWebResponse)hwrq.GetResponse())
                {
                    LastModified = response.LastModified.ToFileTime().ToString();
                }
            }
            catch (WebException we)
            {
                throw new WebException(string.Format("FtpGetLastModified:\r\n{0}", we.GetErrorString()));
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("Ftp获取文件最后修改时间时异常:\r\n{0}", e.GetErrorString()));
            }
        }

        /// <summary>
        /// 填充片断字节数组=>调用生成片断文件方法
        /// </summary>
        /// <param name="segmentObj"></param>
        private void FillSenmentData(object segmentObj)
        {
            var segment = segmentObj as FileSegment;
            try
            {
                //判断片断文件是否存在
                if (File.Exists(segment.SegmentFullName))
                {
                    using (FileStream smtfs = File.Open(segment.SegmentFullName, FileMode.Open))
                    {
                        segment.From += (int)smtfs.Length;
                    }
                }
                if (segment.From < segment.To - 1)
                {
                    try
                    {
                        var request = (FtpWebRequest)WebRequest.Create(DownloadFileUrl);
                        request.Credentials = new NetworkCredential(_userName, _password);
                        request.KeepAlive = false;
                        request.Method = WebRequestMethods.Ftp.DownloadFile;
                        request.Timeout = TimeOut;
                        request.UseBinary = true;
                        request.UsePassive = _usePassive;
                        request.ContentOffset = segment.From;
                        try
                        {
                            using (var response = (FtpWebResponse)request.GetResponse())
                            using (var rsStream = response.GetResponseStream())
                            {
                                int offSet = 0;
                                segment.Data = new byte[segment.Lenght];
                                do
                                {
                                    if (segment.ManualResetEvent.WaitOne())
                                    {
                                        var buffer = new byte[segment.Lenght];
                                        var realReadPos = rsStream.Read(buffer, 0, buffer.Length);
                                        var trueOffSet = (segment.Lenght - offSet);/*trueOffSet解决realReadPos值大于segment.Data剩余空间时Buffer.BlockCopy语句异常问题;
                                                                                * 此异常一般在下载文件的最后一个片段时容易出现;
                                                                                * 异常信息如下:
                                                                                偏移量和长度超出数组的界限，或者计数大于从索引到源集合结尾处的元素数量。*/
                                        //填充片断字节数组
                                        var realySize = realReadPos <= trueOffSet ? realReadPos : trueOffSet;
                                        Buffer.BlockCopy(buffer, 0, segment.Data, offSet, realySize);
                                        offSet += realySize;
                                    }
                                } while (offSet < segment.Lenght);
                            }
                        }
                        catch (WebException)
                        {
                        }

                    }
                    catch (Exception ftpEx)
                    {
                        throw new Exception(
                            $"填充片断文件:{segment.SegmentFullName} \r\n字节数组时异常.",
                            ftpEx);
                    }
                }
                else
                {
                    segment.FileNeedToWrite = false;
                }
                WriteSegmentFile(segment);
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("填充片断文件:{0} \r\n字节数组时异常.异常信息如下:\r\n", segment.SegmentFullName), e);
            }
        }

        #endregion
    }
}
