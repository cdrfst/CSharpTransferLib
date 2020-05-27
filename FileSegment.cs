using System.Threading;

namespace TransferLib
{
    /// <summary>
    /// 文件的一个片断
    /// </summary>
    public class FileSegment
    {
        public int From { get; set; }
        public int To { get; set; }

        /// <summary>
        /// 片段数据
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// 用于支持暂停功能
        /// </summary>
        public ManualResetEvent ManualResetEvent
        {
            get;
            set;
        }

        /// <summary>
        /// 片段长度
        /// </summary>
        public int Lenght
        {
            get;
            internal set;
        }

        /// <summary>
        /// 片断数据文件名称
        /// </summary>
        public string SegmentFullName
        {
            get;
            internal set;
        }

        /// <summary>
        /// 片断文件已存在并且完整时该属性值为false;默认为true;
        /// </summary>
        public bool FileNeedToWrite
        {
            get;
            set;
        }

        public FileSegment()
        {
            FileNeedToWrite = true;
        }
    }
}
