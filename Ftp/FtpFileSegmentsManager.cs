namespace TransferLib.Ftp
{
    /// <summary>
    /// Ftp文件碎片管理类
    /// </summary>
    class FtpFileSegmentsManager : FileSegmentsManager
    {
        private static FileSegmentsManager _segmentManagerInstance;
        private static readonly object AsyncRoot = new object();

        #region 创建单例

        /// <summary>
        /// 使用默认的100kb块大小实例化片断管理对象(100kb)
        /// </summary>
        /// <returns></returns>
        public static FileSegmentsManager Create()
        {
            return Create(0);
        }

        /// <summary>
        /// 通过指定的单位块大小实例化片断管理对象(单位kb)
        /// </summary>
        /// <param name="segmentUnit"></param>
        /// <returns></returns>
        public static FileSegmentsManager Create(int segmentUnit)
        {
            if (_segmentManagerInstance != null) return _segmentManagerInstance;
            lock (AsyncRoot)
            {
                if (_segmentManagerInstance == null)
                {
                    if (segmentUnit > 100)
                    {
                        _segmentUnit = segmentUnit * 1024;
                    }
                    return _segmentManagerInstance = new FtpFileSegmentsManager();
                }
                return _segmentManagerInstance;
            }
        }

        #endregion
    }
}
