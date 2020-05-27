using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Aliyun.OSS;
using TransferLib.Aliyun.Model;
using TransferLib.Extensions;

namespace TransferLib.Aliyun
{
    public class FileMultipartUploader
    {

        private static string _endpoint = "http://oss.aliyuncs.com/";
        private static int partSize = 200 * 1024;
        private readonly string _accessKeyId;
        private readonly string _accessKeySecret;
        private OssClient _client;
        private readonly string _key;
        readonly string _fileToUpload;
        public string UploadId { get; private set; }
        private readonly string _bucketName;
        private long _fileLength;
        private bool _isContinued;
        public long TotalParts { get; private set; }

        public delegate void FileUploadCompleteEventHandler(FileMultipartUploader sender, FileUploadEventArgs arg);

        public event FileUploadCompleteEventHandler FileUploadCompleted;
        public event ProgressChangedEventHandler ProgressChanged;
        private List<PartETag> _partsExisted;
        private Exception _exception;
        private ManualResetEvent _mre;

        /// <summary>
        /// 创建一个文件的上传对象
        /// </summary>
        /// <param name="accessKeyId"></param>
        /// <param name="accessKeySecret"></param>
        /// <param name="key"></param>
        /// <param name="fileToUpload"></param>
        /// <param name="bucketName"></param>
        public FileMultipartUploader(string accessKeyId, string accessKeySecret, string key, string fileToUpload, string bucketName)
        {
            _accessKeyId = accessKeyId;
            _accessKeySecret = accessKeySecret;
            _key = key;
            _fileToUpload = fileToUpload;
            _bucketName = bucketName;
            GetFileSize();
            _client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);
            _mre = new ManualResetEvent(false);
        }

        /// <summary>
        /// 异步分片上传。
        /// </summary>
        public void UploadMultipartAsync()
        {
            UploadId = string.IsNullOrEmpty(UploadId) ? InitiateMultipartUpload(_bucketName, _key) : UploadId;
            var act = new Action(UploadPartsAsync);
            act.BeginInvoke(callback =>
            {
                try
                {
                    act.EndInvoke(callback);
                    _mre.WaitOne();
                    if (_exception != null)
                        OnFileUploadCompleted(new FileUploadEventArgs(_exception.GetErrorString(), false));
                    else
                    {
                        OnFileUploadCompleted(new FileUploadEventArgs(null));
                    }
                }
                catch (Exception ex)
                {
                    OnFileUploadCompleted(new FileUploadEventArgs(ex.GetErrorString(), false));
                }
            }, null);
        }

        public void UploadMultipartAsync(string uploadId)
        {
            UploadId = uploadId;
            GetUploadedParts(uploadId);
            UploadMultipartAsync();
        }

        public void Cancel()
        {
            // 删除所有未完成的multipart uploads.  
            ListMultipartUploadsRequest listMultipartUploadsRequest = new ListMultipartUploadsRequest(
                    _bucketName);
            MultipartUploadListing uploadListing = _client.ListMultipartUploads(listMultipartUploadsRequest);
            foreach (MultipartUpload upload in uploadListing.MultipartUploads)
            {
                String key = upload.Key;
                AbortMultipartUploadRequest abortMultipartUploadRequest = new AbortMultipartUploadRequest(
                        _bucketName, key, upload.UploadId);
                _client.AbortMultipartUpload(abortMultipartUploadRequest);
            }
        }

        private Dictionary<int, string> DicEtags = new Dictionary<int, string>();


        //获取所有已上传的片
        private void GetUploadedParts(string uploadId)
        {
            // 初始化OssClient
            var client = new OssClient(_endpoint, _accessKeyId, _accessKeySecret);

            try
            {
                var listPartsRequest = new ListPartsRequest(_bucketName, _key, uploadId);
                var listPartsResult = client.ListParts(listPartsRequest);

                Console.WriteLine("List parts succeeded");

                // 遍历所有Part
                var parts = listPartsResult.Parts;
                foreach (var part in parts)
                {
                    Console.WriteLine("partNumber:{0}, ETag:{1}, Size:{2}", part.PartNumber, part.ETag, part.Size);
                    DicEtags.Add(part.PartNumber, part.ETag);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("List parts failed, {0}", ex.Message);
            }
        }

        #region 计算需要继续上传的片段

        //public void UploadMultipartAsync(string uploadId)
        //{
        //    _partsExisted = ListMultipartUploads(uploadId);
        //    _isContinued = true;
        //    var needUploadPartsNo = NeedUploadParts(_partsExisted);
        //    UploadPartsAsync(uploadId, needUploadPartsNo);
        //}

        private List<int> NeedUploadParts(List<PartETag> partsExisted)
        {
            var needUploadPartsNo = new List<int>();
            for (var i = 1; i <= TotalParts; i++)
            {
                if (partsExisted.Exists(x => x.PartNumber == i)) continue;
                needUploadPartsNo.Add(i);
            }
            return needUploadPartsNo;
        }

        private void UploadPartsAsync(string uploadId, List<int> needUploadPartsNO)
        {
            var ctx = new UploadPartContext()
            {
                BucketName = _bucketName,
                ObjectName = _key,
                UploadId = uploadId,
                TotalParts = TotalParts,
                CompletedParts = 0,
                SyncLock = new object(),
                PartETags = new List<PartETag>()
            };
            foreach (var i in needUploadPartsNO)
            {
                var fs = new FileStream(_fileToUpload, FileMode.Open, FileAccess.Read, FileShare.Read);
                var skipBytes = (long)partSize * (i - 1);
                fs.Seek(skipBytes, 0);
                var size = (partSize < _fileLength - skipBytes) ? partSize : (_fileLength - skipBytes);
                var request = new UploadPartRequest(_bucketName, _key, uploadId)
                {
                    InputStream = fs,
                    PartSize = size,
                    PartNumber = i
                };
                _client.BeginUploadPart(request, UploadPartCallback, new UploadPartContextWrapper(ctx, fs, i));
            }
        }

        #endregion


        /// <summary>
        /// 返回当前执行中的Multipart Upload事件
        /// </summary>
        /// <param name="uploadId"></param>
        public List<PartETag> ListMultipartUploads(string uploadId)
        {
            var partsExisted = new List<PartETag>();
            try
            {
                var listPartsRequest = new ListPartsRequest(_bucketName, _key, uploadId);
                var listPartsResult = _client.ListParts(listPartsRequest);
                // 遍历所有Part
                var parts = listPartsResult.Parts;
                partsExisted.AddRange(parts.Select(p => new PartETag(p.PartNumber, p.ETag)));
                while (!listPartsResult.IsTruncated && listPartsResult.NextPartNumberMarker > 0)
                {
                    listPartsRequest.PartNumberMarker = listPartsResult.NextPartNumberMarker;
                    listPartsResult = _client.ListParts(listPartsRequest);
                    parts = listPartsResult.Parts;
                    partsExisted.AddRange(parts.Select(p => new PartETag(p.PartNumber, p.ETag)));
                }
            }
            catch (Exception ex)
            {
                // ignored
            }
            return partsExisted;
        }

        private string InitiateMultipartUpload(string bucketName, string objectName)
        {
            var request = new InitiateMultipartUploadRequest(bucketName, objectName);
            var result = _client.InitiateMultipartUpload(request);
            return result.UploadId;
        }

        private void GetFileSize()
        {
            var fi = new FileInfo(_fileToUpload);
            _fileLength = fi.Length;
            var partCount = _fileLength / partSize;
            if (_fileLength % partSize != 0)
            {
                partCount++;
            }
            TotalParts = partCount;
        }

        private void UploadPartsAsync()
        {
            try
            {
                var ctx = new UploadPartContext()
                {
                    BucketName = _bucketName,
                    ObjectName = _key,
                    UploadId = UploadId,
                    TotalParts = TotalParts,
                    CompletedParts = 0,
                    SyncLock = new object(),
                    PartETags = new List<PartETag>(),
                    ManualEvent = new ManualResetEvent(false)
                };

                for (var i = 0; i < TotalParts; i++)
                {
                    var fs = new FileStream(_fileToUpload, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var skipBytes = (long)partSize * i;
                    fs.Seek(skipBytes, 0);
                    var size = (partSize < _fileLength - skipBytes) ? partSize : (_fileLength - skipBytes);
                    var request = new UploadPartRequest(_bucketName, _key, UploadId)
                    {
                        InputStream = fs,
                        PartSize = size,
                        PartNumber = i + 1
                    };
                    var iasyncResult = _client.BeginUploadPart(request, UploadPartCallback, new UploadPartContextWrapper(ctx, fs, i + 1));

                    ctx.ManualEvent.WaitOne();
                }
            }
            catch (System.Threading.ThreadAbortException)
            {

            }
            catch (Exception e)
            {
                _exception = e;
            }
        }

        private void UploadPartCallback(IAsyncResult ar)
        {
            try
            {
                var result = _client.EndUploadPart(ar);
                var wrappedContext = (UploadPartContextWrapper)ar.AsyncState;
                wrappedContext.PartStream.Close();

                var ctx = wrappedContext.Context;
                lock (ctx.SyncLock)
                {
                    var partETags = ctx.PartETags;
                    partETags.Add(new PartETag(wrappedContext.PartNumber, result.ETag));
                    ctx.CompletedParts++;
                    //Console.WriteLine("finish {0}/{1}", ctx.CompletedParts, ctx.TotalParts);
                    OnProgressChanged((int)ctx.CompletedParts, null);
                    if (ctx.CompletedParts == ctx.TotalParts || (_isContinued && _partsExisted.Count + ctx.CompletedParts == ctx.TotalParts))
                    {
                        var completeMultipartUploadRequest = new CompleteMultipartUploadRequest(ctx.BucketName, ctx.ObjectName, ctx.UploadId);

                        if (_isContinued && _partsExisted.Count > 0)
                        {
                            partETags.AddRange(_partsExisted);
                        }
                        partETags.Sort((e1, e2) => (e1.PartNumber - e2.PartNumber));
                        foreach (var partETag in partETags)
                        {
                            completeMultipartUploadRequest.PartETags.Add(partETag);
                        }
                        var completeMultipartUploadResult = _client.CompleteMultipartUpload(completeMultipartUploadRequest);
                        Console.WriteLine(@"异步分片上传结果 : " + completeMultipartUploadResult.Location);
                        _isContinued = false;
                        _mre.Set();
                    }
                    ctx.ManualEvent.Set();
                }
            }
            catch (Exception e)
            {
                _exception = e;
            }
        }

        private void OnFileUploadCompleted(FileUploadEventArgs args)
        {
            if (FileUploadCompleted != null)
                FileUploadCompleted(this, args);
        }

        private void OnProgressChanged(int progressPercentage, object userState)
        {
            if (ProgressChanged != null)
                ProgressChanged(this, new ProgressChangedEventArgs(progressPercentage, userState));
        }
    }
}
