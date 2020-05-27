using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TransferLib.Aliyun
{
    public class UploadPartContextWrapper
    {
        public UploadPartContext Context { get; set; }
        public int PartNumber { get; set; }
        public Stream PartStream { get; set; }

        public UploadPartContextWrapper(UploadPartContext context, Stream partStream, int partNumber)
        {
            Context = context;
            PartStream = partStream;
            PartNumber = partNumber;
        }
    }
}
