using System;
using System.Collections.Generic;
using System.IO;
using TransferLib.Extensions;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Net;

namespace TransferLib
{
    /// <summary>
    /// 单个文件下载抽象类
    /// </summary>
    public abstract class FileDownloader : IDisposable
    {
        #region 委托事件
        public delegate void FileDownloadEventHandler(object sender, FileDownLoadCompletedEventArgs e);
        /// <summary>
        /// 下载时的进度通知
        /// </summary>
        public event ProgressChangedEventHandler ProgressChanged;
        /// <summary>
        /// 整个文件下载完成事件
        /// </summary>
        public event FileDownloadEventHandler FileDownloadCompleted;
        #endregion

        #region 构造函数

        static FileDownloader()
        {
            System.Net.ServicePointManager.DefaultConnectionLimit = 512;
        }

        protected FileDownloader()
        {
            try
            {
                TempDir = Environment.GetEnvironmentVariable("TEMP");
            }
            catch
            {
                TempDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
        }

        #endregion

        #region 字段属性

        ///// <summary>
        ///// 网络下载异常后重试次数默认3
        ///// </summary>
        //protected static int RetryTimes = 3;

        /// <summary>
        /// 网络连接超时时间
        /// </summary>
        protected static int TimeOut = 50000;

        /// <summary>
        /// 文件的网络下载路径
        /// </summary>
        protected string DownloadFileUrl;

        /// <summary>
        /// 根据最小单位拆分后的单个文件片段集合
        /// </summary>
        protected virtual Lazy<List<FileSegment>> Segments { get; set; }

        /// <summary>
        /// 当前文件正在下载的任务片段
        /// </summary>
        protected virtual Lazy<List<Task>> SegmentRunningTasks { get; set; }

        /// <summary>
        /// 文件当前下载状态
        /// </summary>
        public virtual TransferState State { get; set; }

        /// <summary>
        /// 单个文件下载时使用的最大线程数量,默认1
        /// </summary>
        protected int MaxThreadCount = 1;

        /// <summary>
        /// 下载过程中临时文件的存放路径
        /// </summary>
        protected virtual string TempDir { get; set; }

        /// <summary>
        /// 文件的最后修改时间
        /// </summary>
        protected virtual string LastModified { get; set; }

        /// <summary>
        /// 当前文件已经下载的字节数量
        /// </summary>
        public virtual int OffSet { get; private set; }

        /// <summary>
        /// 获取下载的文件总长度
        /// </summary>
        public virtual int FileLength { get; set; }

        /// <summary>
        /// 下载的文件名
        /// </summary>
        public virtual string FileName { get; set; }

        /// <summary>
        /// 文件后缀名
        /// </summary>
        public virtual string ExtensionName { get; set; }

        /// <summary>
        /// 每个下载文件的片段管理类
        /// </summary>
        internal FileSegmentsManager FileSegmentsManager { get; set; }

        protected object UserState;

        /// <summary>
        /// 禁用断点续传
        /// </summary>
        public bool BreakpointResumeDisabled { get; set; }

        #endregion

        #region 事件触发器
        /// <summary>
        /// 用于触发下载进度事件ProgressChanged
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnProgressChanged(ProgressChangedEventArgs e)
        {
            try
            {
                ProgressChanged?.Invoke(this, e);
            }
            catch (Exception)
            {
                //ignore
            }
        }
        /// <summary>
        /// 用于触发文件下载完成事件FileDownloadCompletedEvent
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnFileDownloadCompletedEventHandler(FileDownLoadCompletedEventArgs e)
        {
            FileDownloadCompleted?.Invoke(this, e);
        }

        #endregion

        #region 供派生类使用的方法

        /// <summary>
        /// 返回文件名(Item1);扩展名(Item2)
        /// </summary>
        /// <param name="downloadFileUrl">完整的文件下载路径</param>
        /// <returns></returns>
        protected virtual Tuple<string, string> GetFileInfo(string downloadFileUrl)
        {
            var charIndex = downloadFileUrl.LastIndexOf('/');
            string fileName = Guid.NewGuid().ToString("N");
            if (downloadFileUrl.Length == charIndex) return new Tuple<string, string>(fileName, "unknown");
            var dotindex = downloadFileUrl.LastIndexOf('.');
            var fileNameLength = dotindex - charIndex - 1;
            string extensionName = null;
            if (fileNameLength > 0)
            {
                fileName = downloadFileUrl.Substring(charIndex + 1, fileNameLength);
                if (downloadFileUrl.IndexOf('?') > 0)
                {
                    extensionName = downloadFileUrl.Substring(dotindex + 1, downloadFileUrl.IndexOf('?') - dotindex - 1);
                }
                else
                {
                    extensionName = downloadFileUrl.Substring(dotindex + 1);
                }
            }
            else
            {
                fileName = downloadFileUrl.Substring(charIndex + 1);
            }
            return new Tuple<string, string>(fileName, extensionName);
        }

        /// <summary>
        /// 生成片断文件
        /// </summary>
        /// <param name="segment"></param>
        protected virtual void WriteSegmentFile(FileSegment segment)
        {
            lock (this)
            {
                OffSet += segment.Lenght;
                OnProgressChanged(new ProgressChangedEventArgs((int)Math.Floor(100f * OffSet / this.FileLength), UserState));
            }
            if (!segment.FileNeedToWrite && FileLength != OffSet) return;
            if (segment.FileNeedToWrite)
            {
                using (var fs = new FileStream(segment.SegmentFullName, FileMode.Create))
                {
                    fs.Seek(0, SeekOrigin.Current);
                    fs.Write(segment.Data, 0, segment.Data.Length);
                }
            }
            if (FileLength == OffSet)
            {
                CombineTmpFiles();
            }
        }

        /// <summary>
        /// 合并文件片段生成目标文件
        /// </summary>
        protected virtual void CombineTmpFiles()
        {
            var tempDir = Path.Combine(TempDir, FileName, LastModified);
            var targetFileName = string.IsNullOrEmpty(ExtensionName) ? Path.Combine(tempDir, FileName) : Path.Combine(tempDir, FileName) + "." + ExtensionName;

            try
            {
                var buffer = new byte[1024];
                using (var outStream = new FileStream(targetFileName, FileMode.Create))
                {
                    foreach (var srcStream in Segments.Value.Select(segment => new FileStream(segment.SegmentFullName, FileMode.Open)))
                    {
                        int readedLen;
                        while ((readedLen = srcStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            outStream.Write(buffer, 0, readedLen);
                        }
                        srcStream.Close();
                    }
                }
                ClearTempFiles(Segments.Value);
                OnFileDownloadCompletedEventHandler(new FileDownLoadCompletedEventArgs(TransferState.Completed, targetFileName, UserState));
            }
            catch (Exception ex)
            {
                OnFileDownloadCompletedEventHandler(new FileDownLoadCompletedEventArgs(TransferState.Error, string.Format("合并文件:{0}时异常:\r\n{1}", targetFileName, ex.GetErrorString()), UserState));
            }
        }

        /// <summary>
        /// 清理下载期间的临时文件
        /// </summary>
        /// <param name="segments"></param>
        protected virtual void ClearTempFiles(List<FileSegment> segments)
        {
            segments.ForEach(item =>
            {
                if (!File.Exists(item.SegmentFullName)) return;
                try
                {
                    File.SetAttributes(item.SegmentFullName, FileAttributes.Normal);
                    File.Delete(item.SegmentFullName);
                }
                catch
                {
                    // ignored
                }
            });
        }

        #endregion

        #region 公开方法

        public virtual void Dispose()
        {
            if (Segments.IsValueCreated && Segments.Value != null && Segments.Value.Count > 0)
            {
                Segments.Value.Clear();
            }
            if (SegmentRunningTasks.IsValueCreated && SegmentRunningTasks.Value != null && SegmentRunningTasks.Value.Count > 0)
            {
                SegmentRunningTasks.Value.Clear();
            }
        }
        /// <summary>
        /// 暂停下载
        /// </summary>
        public virtual void Suspend()
        {
            if (Segments.IsValueCreated && Segments.Value != null && Segments.Value.Count > 0)
            {
                Segments.Value.ForEach(item =>
                {
                    item.ManualResetEvent.Reset();
                });
            }
        }
        /// <summary>
        /// 用于暂停后的恢复下载
        /// </summary>
        public virtual void Resume()
        {
            {
                if (Segments.IsValueCreated && Segments.Value != null && Segments.Value.Count > 0)
                {
                    Segments.Value.ForEach(item =>
                    {
                        item.ManualResetEvent.Set();
                    });
                }
            }
        }

        /// <summary>
        /// 开始下载文件
        /// </summary>
        public abstract void DownLoadFileAsync(object userState = null);

        #endregion
    }

}
