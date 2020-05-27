using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using TransferLib.Extensions;
using System.Threading.Tasks;

namespace TransferLib.Http
{
    public sealed class HttpFileDownloader : FileDownloader
    {

        #region 构造函数

        /// <summary>
        /// 创建Http文件下载实例
        /// (异常:System.Exception)
        /// </summary>
        /// <param name="downloadFileUrl"></param>
        public HttpFileDownloader(string downloadFileUrl)
        {
            DownloadFileUrl = downloadFileUrl;
            TempDir = Path.Combine(TempDir, "uclass", "Http");
            Segments = new Lazy<List<FileSegment>>();
            SegmentRunningTasks = new Lazy<List<Task>>();
            var fileInfo = GetFileInfo(downloadFileUrl);
            FileName = fileInfo.Item1;
            ExtensionName = fileInfo.Item2;
            GetFileSize();
        }

        /// <summary>
        /// 创建Http文件下载实例
        /// (异常:System.Exception)
        /// </summary>
        /// <param name="downloadFileUrl"></param>
        /// <param name="maxThreadCount">用于下载单个文件的线程数</param>
        public HttpFileDownloader(string downloadFileUrl, int maxThreadCount)
            : this(downloadFileUrl)
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
                FileSegmentsManager = HttpFileSegmentsManager.Create();
                FileSegmentsManager.AddFile(TempDir, FileName, FileLength,LastModified);
                Segments.Value.AddRange(FileSegmentsManager.GetSegmentsByFileName(FileName));
                var sgmtGroupList = Segments.Value.Take(MaxThreadCount);
                int skipCount = 0;
                while (sgmtGroupList.Any())
                {
                    foreach (var segment in sgmtGroupList)
                    {
                        var cts = new System.Threading.CancellationTokenSource();
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
                        OnFileDownloadCompletedEventHandler(new FileDownLoadCompletedEventArgs(TransferState.Error, ae.GetErrorString(),this.UserState));
                        break;
                    }
                    catch (Exception e)
                    {
                        OnFileDownloadCompletedEventHandler(new FileDownLoadCompletedEventArgs(TransferState.Error, e.GetErrorString(), this.UserState));
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
                        var request = (HttpWebRequest)WebRequest.Create(DownloadFileUrl);
                        if (segment.Lenght < HttpFileSegmentsManager.SegmentUnit)
                        {
                            request.AddRange(segment.From);
                        }
                        else
                        {
                            request.AddRange(segment.From, segment.To - 1);
                        }
                        request.Timeout = TimeOut;
                        request.Proxy = null;
                        using (var response = (HttpWebResponse)request.GetResponse())
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
                                    Buffer.BlockCopy(buffer, 0, segment.Data, offSet, realReadPos <= trueOffSet ? realReadPos : trueOffSet);
                                    offSet += realReadPos;
                                }
                            } while (offSet < segment.Lenght);
                        }


                    }
                    catch (Exception httpEx)
                    {
                        throw new Exception(
                            $"填充片断文件:{segment.SegmentFullName} \r\n字节数组时异常.",
                            httpEx);
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

        private void GetFileSize()
        {
            try
            {
                var hwrq = (HttpWebRequest)WebRequest.Create(DownloadFileUrl);
                hwrq.Credentials = CredentialCache.DefaultCredentials;
                hwrq.AddRange(0, 0);
                hwrq.Timeout = TimeOut;
                //hwrq.Proxy = null;
                var response = (HttpWebResponse)hwrq.GetResponse();
                if (response.StatusCode == (HttpStatusCode)206)
                {
                    var lastModifiedHeader = response.Headers.Get("Last-Modified");
                    LastModified = DateTime.Parse(lastModifiedHeader).ToFileTimeUtc().ToString();
                    var cr = response.Headers.Get("Content-Range");
                    int index = -1;
                    if (!string.IsNullOrEmpty(cr) && (index = cr.IndexOf('/')) > 0)
                    {
                        cr = cr.Substring(index + 1);
                        FileLength = string.IsNullOrEmpty(cr) ? 0 : Convert.ToInt32(cr);
                    }
                    bool supportContinue = (response.Headers["Accept-Ranges"] != null &
                                        response.Headers["Accept-Ranges"] == "bytes");
                    if (!supportContinue)
                    {
                        throw new System.NotSupportedException(string.Format("当前http服务器:{0} 不支持断点续传", DownloadFileUrl));
                    }
                }
            }
            catch (WebException we)
            {
                throw new WebException(string.Format("HttpGetFileSize:\r\n{0}", we.GetErrorString()));
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("Http获取文件大小时异常:\r\n{0}", e.GetErrorString()));
            }
        }

        #endregion

    }
}
