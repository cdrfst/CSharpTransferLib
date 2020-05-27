using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace TransferLib
{
    /// <summary>
    /// 每个下载文件的片段管理类
    /// </summary>
    internal abstract class FileSegmentsManager
    {
        /// <summary>
        /// 文件和片段对应关系
        /// </summary>
        private readonly Lazy<ConcurrentDictionary<string, List<FileSegment>>> _fileSegmentsMap = new Lazy<ConcurrentDictionary<string, List<FileSegment>>>();

        /// <summary>
        /// 下载的最小单位
        /// </summary>
        protected static int _segmentUnit = 1024 * 200;

        #region 供派生类调用的方法

        /// <summary>
        /// 生成文件片断集合
        /// </summary>
        /// <param name="currentDir"></param>
        /// <param name="fileName"></param>
        /// <param name="fileLength"></param>
        /// <returns></returns>
        protected virtual List<FileSegment> CreateSegments(string currentDir, string fileName, int fileLength)
        {
            var segmentsDes = CalculateSegments(fileLength);
            int pos = 0;
            var segmentList = new List<FileSegment>();
            var tempFile = Path.Combine(currentDir, fileName);
            var manualResetEvent = new ManualResetEvent(true);
            for (int i = 0; i <= segmentsDes.Item2; i++)
            {
                var segment = new FileSegment();
                segment.ManualResetEvent = manualResetEvent;
                if (segmentsDes.Item2 == i)//最后一段数据
                {
                    if (!segmentsDes.Item3.HasValue || (segmentsDes.Item3.Value == 0)) break;
                    segment.Lenght = segmentsDes.Item3.Value;
                    segment.From = pos;
                    segment.To = pos + segmentsDes.Item3.Value + 1;
                    segment.SegmentFullName = string.Format("{0}_{1}.tmp", tempFile, i.ToString());
                    segmentList.Add(segment);
                    break;
                }
                segment.Lenght = segmentsDes.Item1;
                segment.From = pos;
                segment.To = pos + segmentsDes.Item1;
                segment.SegmentFullName = string.Format("{0}_{1}.tmp", tempFile, i.ToString());
                segmentList.Add(segment);
                pos += segmentsDes.Item1;
            }
            return segmentList;
        }

        /// <summary>
        /// 根据配置的最小下载单位和文件大小计算下载片段集合并返回;
        /// Item1单位段大小
        /// Item2被拆分为多少段
        /// Item3剩余的不足1个单位的大小
        /// </summary>
        /// <param name="totalSize"></param>
        /// <returns></returns>
        protected static Tuple<int, int, int?> CalculateSegments(int totalSize)
        {
            // 9319308;
            int segmentCount = totalSize % _segmentUnit;
            Tuple<int, int, int?> result;
            if (segmentCount == 0)
                result = new Tuple<int, int, int?>(_segmentUnit, totalSize / _segmentUnit, null);
            else
            {
                result = new Tuple<int, int, int?>(_segmentUnit, (int)Math.Floor(Convert.ToDouble(totalSize / _segmentUnit)), segmentCount);
            }
            return result;
        }

        #endregion

        #region 私有方法

        private void CheckTempDirectory(string directoryName)
        {
            if (!Directory.Exists(directoryName)) Directory.CreateDirectory(directoryName);
        }

        #endregion

        #region 公开方法

        /// <summary>
        /// 创建文件下载片段
        /// </summary>
        /// <param name="baseTempDir"></param>
        /// <param name="fileName"></param>
        /// <param name="fileLength"></param>
        internal void AddFile(string baseTempDir, string fileName, int fileLength, string lastModified)
        {
            var currentDir = Path.Combine(baseTempDir, fileName, lastModified ?? string.Empty);
            CheckTempDirectory(currentDir);
            var fileSegments = CreateSegments(currentDir, fileName, fileLength);
            _fileSegmentsMap.Value.TryAdd(fileName, fileSegments);
        }

        /// <summary>
        /// 移除文件下载碎片
        /// </summary>
        /// <param name="fileName"></param>
        internal void RemoveFile(string fileName)
        {
            try
            {
                List<FileSegment> segmentList;
                _fileSegmentsMap.Value.TryRemove(fileName, out segmentList);
                segmentList.Clear();
            }
            catch
            {
                //ignore
            }
        }

        /// <summary>
        /// 返回文件下载碎片
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        internal List<FileSegment> GetSegmentsByFileName(string fileName)
        {
            return _fileSegmentsMap.Value.ContainsKey(fileName) ? _fileSegmentsMap.Value[fileName] : null;
        }

        /// <summary>
        /// 设置下载单元大小,默认200 (单位:kb)
        /// </summary>
        /// <param name="segmentUnit"></param>
        public static void SetSegmentUnit(int segmentUnit)
        {
            if (segmentUnit <= 0) throw new ArgumentException(string.Format("设置的参数值:{0} 不正确,该参数必须大于0", segmentUnit), "segmentUnit");
            _segmentUnit = segmentUnit * 1024;
        }

        #endregion

    }
}
